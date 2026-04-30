using Sandbox;
using System;

public sealed class TestControlls : Component
{
	[Property, Group( "Speed" )] public float PullForce { get; set; } = 5f;
	[Property, Group( "Speed" )] public float BrakeForce { get; set; } = 5f;

	[Property, Group( "Speed" )] public float MaxVerticalSpeed { get; set; } = 10f;

	[Property, Group( "Steering" )] public float SteerTorque { get; set; } = 10f;
	[Property, Group( "Steering" )] public float MaxAngularSpeed { get; set; } = 10f;


	[Property, Group("GameObjects")] public Rigidbody Rigidbody { get; set; }

	private Vector2 moveInput;


	protected override void OnUpdate()
	{
		ApplyInputs(); 
	}

	protected override void OnFixedUpdate()
	{
		ApplyLocomotion(moveInput.x);
		ApplySteering( moveInput.y );
	}

	private void ApplyInputs()
	{
		float verticalStrength = 0f;
		float horizontalStrenght = 0f;

		if ( Input.Down( "Forward" ) )   verticalStrength += 1f;
		if ( Input.Down( "Backward" ) )  verticalStrength -= 1f; 
		if (Input.Down("Right")) horizontalStrenght -= 1f;
		if (Input.Down("Left"))	 horizontalStrenght += 1f;

		moveInput = new Vector2( verticalStrength, horizontalStrenght );
	}

	private void ApplyLocomotion(float acceleration)
	{
		Vector3 forward = WorldRotation.Forward;
		float forwardSpeed = Vector3.Dot( Rigidbody.Velocity, forward );

		float accelerationThreshhold = -12f; 

		if (acceleration > accelerationThreshhold && forwardSpeed < MaxVerticalSpeed)
		{
			Rigidbody.ApplyForce( forward * PullForce * acceleration ); 
		}

		else if (acceleration < accelerationThreshhold || forwardSpeed > MaxVerticalSpeed )
		{
			Rigidbody.ApplyForce( -forward * BrakeForce * acceleration );
		}
	}

	private void ApplySteering(float torqueInput)
	{
		Vector3 torque = SteerTorque * torqueInput; 
		Rigidbody.ApplyTorque( torque );
	}
}
