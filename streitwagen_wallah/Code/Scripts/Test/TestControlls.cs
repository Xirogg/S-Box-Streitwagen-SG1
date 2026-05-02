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

	[Sync]	private Vector2 moveInput {  get; set; }


	protected override void OnUpdate()
	{
		// Wenn es nicht der Spieler des Prefabs ist wird alles geskippt
		if ( IsProxy ) return; 


		ApplyInputs(); 
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return; 


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
		if ( acceleration == 0f ) return;

		Vector3 forward = WorldRotation.Forward;
		float forwardSpeed = Vector3.Dot( Rigidbody.Velocity, forward );

		if ( acceleration > 0 && forwardSpeed >= MaxVerticalSpeed ) return;
		if ( acceleration < 0 && forwardSpeed <= -MaxVerticalSpeed ) return;

		float mass = Rigidbody.Mass;
		Rigidbody.ApplyForce( forward * PullForce * acceleration * mass );
	}

	private void ApplySteering(float torqueInput)
	{
		if ( torqueInput == 0f ) return;
		if ( MathF.Abs( Rigidbody.AngularVelocity.z ) >= MaxAngularSpeed ) return;

		float mass = Rigidbody.Mass;
		Rigidbody.ApplyTorque( Vector3.Up * SteerTorque * torqueInput * mass );
	}
}
