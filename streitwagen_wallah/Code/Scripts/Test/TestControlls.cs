using Sandbox;
using System;

public sealed class TestControlls : Component
{
	[Property, Group( "Speed" )] public float PullForce { get; set; } = 5f;
	[Property, Group( "Speed" )] public float BrakeForce { get; set; } = 5f;

	[Property, Group( "Speed" )] public float MaxVerticalSpeed { get; set; } = 10f;

	[Property, Group( "Steering" )] public float SteerTorque { get; set; } = 10f;
	[Property, Group( "Steering" )] public float MaxAngularSpeed { get; set; } = 10f;
	[Property, Group( "Steering" )] public float LateralGrip { get; set; } = 4f;


	[Property, Group("GameObjects")] public Rigidbody Rigidbody { get; set; }

	[Sync]	private Vector2 moveInput {  get; set; }

	[Sync, Property, Group( "Identity" )] public Guid PlayerId { get; set; }

	[Sync] public bool IsDrunk { get; private set; }

	public event Action<bool> OnDrunkChanged;

	public void SetDrunk( bool on )
	{
		if ( IsDrunk == on ) return;
		IsDrunk = on;
		OnDrunkChanged?.Invoke( on );
	}

	protected override void OnStart()
	{
		if ( PlayerId == Guid.Empty )
			PlayerId = Guid.NewGuid();
	}

	protected override void OnUpdate()
	{
		// Wenn es nicht der Spieler des Prefabs ist wird alles geskippt
		if ( IsProxy ) return;


		ApplyInputs();
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return; 


		ApplyHorseLateralGrip();
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

		if ( IsDrunk )
		{
			verticalStrength = -verticalStrength;
			horizontalStrenght = -horizontalStrenght;
		}

		moveInput = new Vector2( verticalStrength, horizontalStrenght );
	}

	private void ApplyLocomotion(float acceleration)
	{
		if ( acceleration == 0f ) return;

		Vector3 forward = WorldRotation.Forward;
		float forwardSpeed = Vector3.Dot( Rigidbody.Velocity, forward );
		float planarSpeed = Rigidbody.Velocity.WithZ( 0f ).Length;

		if ( acceleration > 0 && planarSpeed >= MaxVerticalSpeed && forwardSpeed > 0 ) return;
		if ( acceleration < 0 && forwardSpeed <= -MaxVerticalSpeed ) return;

		float mass = Rigidbody.Mass;
		Rigidbody.ApplyForce( forward * PullForce * acceleration * mass );
	}

	private void ApplyHorseLateralGrip()
	{
		if ( LateralGrip <= 0f ) return;

		Vector3 right = WorldRotation.Right;
		float lateral = Vector3.Dot( Rigidbody.Velocity, right );
		float kill = 1f - MathF.Exp( -LateralGrip * Time.Delta );
		Rigidbody.Velocity -= right * (lateral * kill);
	}

	private void ApplySteering(float torqueInput)
	{
		if ( torqueInput == 0f ) return;
		if ( MathF.Abs( Rigidbody.AngularVelocity.z ) >= MaxAngularSpeed ) return;

		float mass = Rigidbody.Mass;
		Rigidbody.ApplyTorque( Vector3.Up * SteerTorque * torqueInput * mass );
	}
}
