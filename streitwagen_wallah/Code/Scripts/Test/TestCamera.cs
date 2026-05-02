using Sandbox;
using System; 
public sealed class TestCamera : Component
{
	[Property, Group( "Target" )] private GameObject TargetGO { get; set; }

	[Property, Group( "Camera Settings" )] public float SmoothSpeed { get; set; } = 10f;
	[Property, Group( "Camera Settings" )] public float LookHeight { get; set; } = 50f;

	// s&box uses Z-up. X = forward/back, Y = left/right, Z = up.
	// Default: 200 units behind the target, 50 units up.
	[Property, Group( "Camera Settings" )] public Vector3 CameraOffset { get; set; } = new Vector3( -200f, 0f, 50f );

	protected override void OnUpdate()
	{
		ApplyCameraFollow();
	}

	private bool _initialized;

	private void ApplyCameraFollow()
	{
		if ( TargetGO is null )
			return;

		// Only follow the target's yaw so pitch/roll of the target don't tilt the camera.
		Angles targetAngles = TargetGO.WorldRotation.Angles();
		Rotation yawOnlyRotation = Rotation.FromYaw( targetAngles.yaw );

		// Rotate the offset into the target's yaw frame, then add to its world position.
		Vector3 desiredPosition = TargetGO.WorldPosition + yawOnlyRotation * CameraOffset;

		// Snap to the desired position on the very first update so we don't lerp from
		// (0,0,0) — that would make the camera briefly look straight up.
		if ( !_initialized )
		{
			WorldPosition = desiredPosition;
			_initialized = true;
		}
		else
		{
			// Frame-rate independent smoothing. Higher SmoothSpeed = snappier follow.
			float t = 1f - MathF.Exp( -MathF.Max( 0f, SmoothSpeed ) * Time.Delta );
			WorldPosition = Vector3.Lerp( WorldPosition, desiredPosition, t );
		}

		// Aim at the target. Use an explicit world up so LookAt produces a level horizon.
		Vector3 lookTarget = TargetGO.WorldPosition + Vector3.Up * LookHeight;
		Vector3 lookDir = lookTarget - WorldPosition;
		if ( lookDir.LengthSquared > 0.0001f )
			WorldRotation = Rotation.LookAt( lookDir.Normal, Vector3.Up );
	}
}
