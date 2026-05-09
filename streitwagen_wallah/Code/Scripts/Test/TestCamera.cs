using Sandbox;
using System;
using static Sandbox.ModelPhysics;

/// <summary>
/// Dynamic third-person chariot camera. Yaw-only follow (bumps/roll of the chassis
/// don't tilt the cam) plus speed FOV punch, velocity look-ahead, and drift lean
/// to sell slides. Port/expansion of the Unity ChariotCamera prototype.
/// </summary>
public sealed class TestCamera : Component
{
	[Property, Group( "Target" )] public GameObject TargetGO { get; set; }
	[Property, Group( "Target" )] public ChariotPhysics ChariotPhysics { get; set; }

	[Property, Group( "Follow" )] public Vector3 Offset { get; set; } = new Vector3( -280f, 0f, 140f );
	[Property, Group( "Follow" )] public float PositionSmoothTime { get; set; } = 0.25f;
	[Property, Group( "Follow" )] public float RotationSmoothSpeed { get; set; } = 6f;
	[Property, Group( "Follow" )] public float LookHeight { get; set; } = 40f;

	[Property, Group( "Look-Ahead" )] public float LookAheadFactor { get; set; } = 0.18f;
	[Property, Group( "Look-Ahead" )] public float LookAheadMax { get; set; } = 300f;

	[Property, Group( "Speed FOV" )] public float BaseFOV { get; set; } = 70f;
	[Property, Group( "Speed FOV" )] public float MaxFOV { get; set; } = 100f;
	[Property, Group( "Speed FOV" )] public float MaxSpeed { get; set; } = 1500f;
	[Property, Group( "Speed FOV" )] public float FovCurvePower { get; set; } = 1.6f;
	[Property, Group( "Speed FOV" )] public float FovLerpSpeed { get; set; } = 6f;

	[Property, Group( "Drift" )] public float DriftDeadzone { get; set; } = 8f;
	[Property, Group( "Drift" )] public float DriftMaxAngle { get; set; } = 35f;
	[Property, Group( "Drift" )] public float DriftLateralMax { get; set; } = 120f;
	[Property, Group( "Drift" )] public float DriftRollMax { get; set; } = 8f;
	[Property, Group( "Drift" )] public float DriftLerpSpeed { get; set; } = 5f;
	[Property] public ParticleEmitter SpeedLines { get; set; }

	private Vector3 _posVelocity;
	private Rigidbody _targetRb;
	private CameraComponent _cam;
	private float _currentRoll;
	private float _currentDriftLateral;
	private bool _initialized;

	protected override void OnStart()
	{
		if ( TargetGO is not null )
			_targetRb = TargetGO.Components.Get<Rigidbody>();

		_cam = Components.Get<CameraComponent>();

		if ( IsProxy )
		{
			if ( _cam is not null )
				_cam.Enabled = false;
			return;
		}

		if ( _cam is not null )
			_cam.FieldOfView = BaseFOV;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( TargetGO is null ) return;

		Angles targetAng = TargetGO.WorldRotation.Angles();
		Rotation yawOnly = Rotation.FromYaw( targetAng.yaw );

		// Planar velocity (ignore vertical bumps so jumps don't widen FOV / fake drift)
		Vector3 vel = _targetRb is not null ? _targetRb.Velocity.WithZ( 0f ) : Vector3.Zero;
		float planarSpeed = vel.Length;

		// Drift: prefer ChariotPhysics telemetry, fall back to velocity-vs-forward angle
		float driftAngle = ChariotPhysics is not null
			? ChariotPhysics.DriftAngle
			: (planarSpeed > 5f ? Vector3.GetAngle( vel, yawOnly.Forward ) : 0f);

		float lateralVel = Vector3.Dot( vel, yawOnly.Right );
		float driftSign = MathF.Sign( lateralVel );
		float driftRange = MathF.Max( 0.001f, DriftMaxAngle - DriftDeadzone );
		float driftMag = Math.Clamp( (MathF.Abs( driftAngle ) - DriftDeadzone) / driftRange, 0f, 1f );

		// Smooth the lateral offset & roll so they don't snap when drift toggles
		float t = 1f - MathF.Exp( -MathF.Max( 0f, DriftLerpSpeed ) * Time.Delta );
		float targetLateral = driftSign * driftMag * DriftLateralMax;
		_currentDriftLateral = MathX.Lerp( _currentDriftLateral, targetLateral, t );
		float targetRoll = -driftSign * driftMag * DriftRollMax;
		_currentRoll = MathX.Lerp( _currentRoll, targetRoll, t );

		Vector3 desiredPosition = TargetGO.WorldPosition
			+ yawOnly * Offset
			+ yawOnly.Right * _currentDriftLateral;

		if ( !_initialized )
		{
			WorldPosition = desiredPosition;
			_initialized = true;
		}
		else
		{
			WorldPosition = SmoothDamp( WorldPosition, desiredPosition, ref _posVelocity, PositionSmoothTime, Time.Delta );
		}

		// Look target with velocity look-ahead
		Vector3 lookTarget = TargetGO.WorldPosition + Vector3.Up * LookHeight;
		if ( _targetRb is not null )
		{
			Vector3 lookAhead = _targetRb.Velocity * LookAheadFactor;
			if ( lookAhead.Length > LookAheadMax )
				lookAhead = lookAhead.Normal * LookAheadMax;
			lookTarget += lookAhead;
		}

		Vector3 lookDir = lookTarget - WorldPosition;
		if ( lookDir.LengthSquared > 0.001f )
		{
			Angles a = Rotation.LookAt( lookDir, Vector3.Up ).Angles();
			a.roll = _currentRoll;
			Rotation desired = a.ToRotation();
			WorldRotation = Rotation.Slerp( WorldRotation, desired, RotationSmoothSpeed * Time.Delta );
		}

		if ( SpeedLines is not null )
			SpeedLines.Enabled = planarSpeed > 800f;

		UpdateFOV( planarSpeed );
	}

	private void UpdateFOV( float planarSpeed )
	{
		if ( _cam is null ) return;

		float ratio = MaxSpeed > 0.01f ? Math.Clamp( planarSpeed / MaxSpeed, 0f, 1f ) : 0f;
		float curved = MathF.Pow( ratio, FovCurvePower );
		float targetFOV = MathX.Lerp( BaseFOV, MaxFOV, curved );

		float t = 1f - MathF.Exp( -MathF.Max( 0f, FovLerpSpeed ) * Time.Delta );
		_cam.FieldOfView = MathX.Lerp( _cam.FieldOfView, targetFOV, t );
	}

	private static Vector3 SmoothDamp( Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float deltaTime )
	{
		smoothTime = MathF.Max( 0.0001f, smoothTime );
		float omega = 2f / smoothTime;
		float x = omega * deltaTime;
		float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);

		Vector3 change = current - target;
		Vector3 temp = (currentVelocity + omega * change) * deltaTime;
		currentVelocity = (currentVelocity - omega * temp) * exp;
		return target + (change + temp) * exp;
	}
}
