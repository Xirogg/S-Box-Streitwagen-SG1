using Sandbox;
using System;

/// <summary>
/// Third-Person-Kamera für den Streitwagen. Folgt dem Target nur in Yaw — Pitch/Roll
/// des Targets werden ignoriert, damit die Kamera bei Stössen / Drift nicht kippt.
/// Offset wird im Yaw-only-Frame des Targets angewendet.
/// </summary>
public sealed class ChariotCamera : Component
{
	[Property, Group( "Target" )] public GameObject Target { get; set; }

	[Property, Group( "Follow" )] public Vector3 Offset { get; set; } = new Vector3( -280f, 0f, 140f );
	[Property, Group( "Follow" )] public float PositionSmoothTime { get; set; } = 0.25f;
	[Property, Group( "Follow" )] public float RotationSmoothSpeed { get; set; } = 6f;
	[Property, Group( "Follow" )] public float LookAheadFactor { get; set; } = 0.15f;
	[Property, Group( "Follow" )] public float LookAheadMax { get; set; } = 250f;
	[Property, Group( "Follow" )] public float LookHeight { get; set; } = 40f;

	private Vector3 _currentVelocity;
	private Rigidbody _targetRb;

	public void SetTarget( GameObject newTarget )
	{
		Target = newTarget;
		_targetRb = newTarget?.Components.Get<Rigidbody>();
	}

	protected override void OnStart()
	{
		if ( Target is not null )
			_targetRb = Target.Components.Get<Rigidbody>();
	}

	protected override void OnUpdate()
	{
		if ( Target is null ) return;

		Angles targetAng = Target.WorldRotation.Angles();
		Rotation yawOnly = Rotation.FromYaw( targetAng.yaw );

		Vector3 desiredPosition = Target.WorldPosition + yawOnly * Offset;
		WorldPosition = SmoothDamp( WorldPosition, desiredPosition, ref _currentVelocity, PositionSmoothTime, Time.Delta );

		Vector3 lookTarget = Target.WorldPosition + Vector3.Up * LookHeight;
		if ( _targetRb is not null )
		{
			Vector3 vel = _targetRb.Velocity;
			Vector3 lookAhead = vel * LookAheadFactor;
			if ( lookAhead.Length > LookAheadMax )
				lookAhead = lookAhead.Normal * LookAheadMax;
			lookTarget += lookAhead;
		}

		Vector3 lookDir = lookTarget - WorldPosition;
		if ( lookDir.LengthSquared > 0.001f )
		{
			Rotation desired = Rotation.LookAt( lookDir, Vector3.Up );
			WorldRotation = Rotation.Slerp( WorldRotation, desired, RotationSmoothSpeed * Time.Delta );
		}
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
