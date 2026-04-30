using Sandbox;
using System;
using static Sandbox.PhysicsContact;

public sealed class TestCamera : Component
{
	[Property, Group( "Target" )] private GameObject TargetGO { get; set;  }

	[Property, Group( "Camera Settings" )] public float SmoothTime { get; set;  }
	[Property, Group( "Camera Settings" )] public float SmoothSpeed { get; set; }
	[Property, Group( "Camera Settings" )] public float LookHeight { get; set; }
	[Property, Group( "Camera Settings" )] public Vector3 CameraOffset { get; set; } = new Vector3( -200f, 50f, 0f );

	private Vector3 speed; 

	protected override void OnUpdate()
	{
		ApplyCameraFollow(); 
	}
	



	private void ApplyCameraFollow()
	{

		Angles targetAngles = TargetGO.WorldRotation.Angles();
		Rotation yawOnlyRotation = Rotation.FromYaw( targetAngles.yaw );

		Vector3 desiredPosition = TargetGO.WorldPosition * yawOnlyRotation * CameraOffset;
		WorldPosition = SmoothDamp( WorldPosition, desiredPosition, ref speed, SmoothTime, Time.Delta ); 
	}





	private static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float deltaTime)
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

