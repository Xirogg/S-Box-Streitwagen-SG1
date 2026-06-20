using Sandbox;
using System;
using System.Collections.Generic;
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

	// How far ahead of the chariot the camera looks, scaled by velocity (0 = no look-ahead, 0.2 = subtle, 0.5 = aggressive)
	[Property, Group( "Look-Ahead" )] public float LookAheadStrength { get; set; } = 0.18f;

	[Property, Group( "Speed FOV" )] public float BaseFOV { get; set; } = 70f;
	[Property, Group( "Speed FOV" )] public float MaxFOV { get; set; } = 100f;
	[Property, Group( "Speed FOV" )] public float FovLerpSpeed { get; set; } = 6f;

	// Drift tuning is intentionally hardcoded to keep the editor simple. Tweak here if needed.
	private const float DriftDeadzone = 8f;
	private const float DriftMaxAngle = 35f;
	private const float DriftLateralMax = 120f;
	private const float DriftRollMax = 8f;
	private const float DriftLerpSpeed = 5f;

	// FOV ramp shape: 1 = linear, >1 = FOV stays calm at low speed and ramps hard near top speed.
	private const float FovCurvePower = 1.6f;

	// Hard cap on look-ahead distance so the camera doesn't aim off-screen at very high speeds.
	private const float LookAheadMax = 300f;

	[Property] public BlitOverlay SpeedLines { get; set; }

	private Vector3 _posVelocity;
	private Rigidbody _targetRb;
	private CameraComponent _cam;
	private float _currentRoll;
	private float _currentDriftLateral;
	private bool _initialized;

	//Road Shake
	private float _shakeTime;
	private float _prevSpeed;
	[Property, Group( "Road Shake" )] public float ShakeIntensity { get; set; } = 1.2f;
	[Property, Group( "Road Shake" )] public float ShakeFrequency { get; set; } = 18f;
	[Property, Group( "Road Shake" )] public float ShakeMaxSpeed { get; set; } = 1500f;

	// G-Force Nod
	private float _currentNod;
	[Property, Group( "G-Force" )] public float NodMax { get; set; } = 4f;
	[Property, Group( "G-Force" )] public float NodLerpSpeed { get; set; } = 8f;

	// Impact Shake — triggered by chariot collisions, scales with closing speed
	[Property, Group( "Cam Shake" )] public float ImpactShakeMax { get; set; } = 8f;
	[Property, Group( "Cam Shake" )] public float ImpactShakeDecay { get; set; } = 5f;
	[Property, Group( "Cam Shake" )] public float ImpactMinClosingSpeed { get; set; } = 100f;
	[Property, Group( "Cam Shake" )] public List<string> IgnoredImpactTags { get; set; } = new() { "ground", "terrain" };

	private float _impactShake;
	private readonly Random _impactRng = new();

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

		if ( ChariotPhysics is not null )
			ChariotPhysics.ImpactStarted += HandleImpact;
	}

	protected override void OnDestroy()
	{
		if ( ChariotPhysics is not null )
			ChariotPhysics.ImpactStarted -= HandleImpact;
	}

	private void HandleImpact( Collision collision )
	{
		if ( IsProxy ) return;
		if ( collision.Other.GameObject is null ) return;

		// Skip ignored surfaces (ground, terrain, etc.) — check both the hit GO and its root
		var hitGo = collision.Other.GameObject;
		foreach ( var tag in IgnoredImpactTags )
		{
			if ( hitGo.Tags.Has( tag ) ) return;
			if ( hitGo.Root is not null && hitGo.Root.Tags.Has( tag ) ) return;
		}

		// Closing speed = how fast we were heading INTO the surface along the contact normal
		Vector3 myVel = _targetRb is not null ? _targetRb.Velocity : Vector3.Zero;
		float closingSpeed = MathF.Abs( Vector3.Dot( myVel, -collision.Contact.Normal ) );
		if ( closingSpeed < ImpactMinClosingSpeed ) return;

		float maxSpeed = ChariotPhysics is not null ? ChariotPhysics.MaxSpeed : 1500f;
		float norm = Math.Clamp( closingSpeed / maxSpeed, 0f, 1f );
		_impactShake = MathF.Max( _impactShake, norm * ImpactShakeMax );
	}

	private void ApplyImpactShake()
	{
		if ( _impactShake < 0.01f ) return;

		float dx = ((float)_impactRng.NextDouble() * 2f - 1f) * _impactShake;
		float dy = ((float)_impactRng.NextDouble() * 2f - 1f) * _impactShake;
		float dz = ((float)_impactRng.NextDouble() * 2f - 1f) * _impactShake;
		WorldPosition += new Vector3( dx, dy, dz );

		_impactShake *= MathF.Exp( -ImpactShakeDecay * Time.Delta );
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
			Vector3 lookAhead = _targetRb.Velocity * LookAheadStrength;
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
			SpeedLines.Enabled = planarSpeed > 1000f;

		UpdateFOV( planarSpeed );
		ApplyRacerShake( planarSpeed );
		ApplyImpactShake();
	}

	private void UpdateFOV( float planarSpeed )
	{
		if ( _cam is null ) return;

		float maxSpeed = ChariotPhysics is not null ? ChariotPhysics.MaxSpeed : 1500f;
		float ratio = maxSpeed > 0.01f ? Math.Clamp( planarSpeed / maxSpeed, 0f, 1f ) : 0f;
		float curved = MathF.Pow( ratio, FovCurvePower );
		float targetFOV = MathX.Lerp( BaseFOV, MaxFOV, curved );

		float t = 1f - MathF.Exp( -MathF.Max( 0f, FovLerpSpeed ) * Time.Delta );
		_cam.FieldOfView = MathX.Lerp( _cam.FieldOfView, targetFOV, t );
	}

	private void ApplyRacerShake( float planarSpeed )
	{
		// --- Road Shake (überlagerte Sinus = unregelmäßig genug für Fahrzeuge) ---
		float intensity = MathX.Remap( planarSpeed, 0f, ShakeMaxSpeed, 0f, 1f );
		_shakeTime += Time.Delta * ShakeFrequency;

		float shakeY = (MathF.Sin( _shakeTime * 1.00f ) * 0.50f
					   + MathF.Sin( _shakeTime * 2.70f ) * 0.30f
					   + MathF.Sin( _shakeTime * 5.13f ) * 0.20f)
					   * ShakeIntensity * intensity;

		float shakeX = (MathF.Sin( _shakeTime * 1.37f ) * 0.50f
					   + MathF.Sin( _shakeTime * 3.10f ) * 0.30f
					   + MathF.Sin( _shakeTime * 6.77f ) * 0.20f)
					   * ShakeIntensity * 0.4f * intensity;

		WorldPosition += WorldRotation.Up * shakeY
					   + WorldRotation.Right * shakeX;

		// --- G-Force Nod (Beschleunigung/Bremsen) ---
		float accel = (planarSpeed - _prevSpeed) / Time.Delta;
		_prevSpeed = planarSpeed;

		float maxSpeed = ChariotPhysics is not null ? ChariotPhysics.MaxSpeed : 1500f;
		float targetNod = Math.Clamp( -accel / maxSpeed * NodMax, -NodMax, NodMax );
		float t = 1f - MathF.Exp( -NodLerpSpeed * Time.Delta );
		_currentNod = MathX.Lerp( _currentNod, targetNod, t );

		// Nod als Pitch-Offset auf die aktuelle Rotation draufrechnen
		Angles a = WorldRotation.Angles();
		a.pitch += _currentNod;
		WorldRotation = a.ToRotation();
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
