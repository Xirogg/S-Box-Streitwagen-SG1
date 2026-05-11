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
	[Property, Group( "Steering" )] public float SharpSteerMultiplier { get; set; } = 1.4f;
	[Property, Group( "Steering" )] public float SteerReleaseDamping { get; set; } = 12f;
	[Property, Group( "Steering" )] public float SteerInputDeadzone { get; set; } = 0.05f;

	[Property, Group( "Ram Lurch" )] public float LurchImpulse { get; set; } = 800f;
	[Property, Group( "Ram Lurch" )] public float LurchForwardOffset { get; set; } = 60f;


	[Property, Group( "GameObjects" )] public Rigidbody Rigidbody { get; set; }

	[Sync] private Vector2 moveInput { get; set; }

	[Sync, Property, Group( "Identity" )] public Guid PlayerId { get; set; }

	[Sync] public bool IsDrunk { get; private set; }

	/// <summary>
	/// True solange der Spieler Left/Right UND zusätzlich RamLeft/RamRight (Q/E) drückt.
	/// Das ist der "scharf einlenken"-Modus — Q/E verstärkt das Lenken nur dann.
	/// </summary>
	[Sync] public bool IsSharpSteering { get; private set; }

	/// <summary>
	/// True wenn der Spieler RamLeft/RamRight (Q/E) drückt OHNE gleichzeitig Left/Right zu halten.
	/// In diesem Fall wird nicht gelenkt — stattdessen versucht der Spieler einen Wagen zu rammen
	/// (für PlayerCollisions o.ä. gedacht).
	/// </summary>
	[Sync] public bool IsRamAttempting { get; private set; }

	/// <summary>
	/// Vorzeichen der RamLeft/RamRight Eingabe: +1 = links (RamLeft/Q), -1 = rechts (RamRight/E), 0 = keine.
	/// Folgt der gleichen Konvention wie die horizontale Eingabe in diesem Skript (Left=+1, Right=-1).
	/// Wird hauptsächlich für VFX/Audio gebraucht — PlayerCollisions reicht meistens IsRamAttempting.
	/// </summary>
	[Sync] public float RamDirection { get; private set; }

	public event Action<bool> OnDrunkChanged;

	public void SetDrunk( bool on )
	{
		if ( IsDrunk == on ) return;
		IsDrunk = on;
		OnDrunkChanged?.Invoke( on );
	}

	protected override void OnStart()
	{
		if ( IsProxy ) return;

		if ( PlayerId == Guid.Empty )
			PlayerId = Guid.NewGuid();
	}

	protected override void OnUpdate()
	{
		// Wenn es nicht der Spieler des Prefabs ist wird alles geskippt
		if ( IsProxy ) return;


		ApplyInputs();
		TryApplyLurch();
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;


		ApplyHorseLateralGrip();
		ApplyLocomotion( moveInput.x );
		ApplySteering( moveInput.y );
	}

	private void ApplyInputs()
	{
		float verticalStrength = 0f;
		float horizontalStrenght = 0f;
		float sharpInputDir = 0f;

		if ( Input.Down( "Forward" ) ) verticalStrength += 1f;
		if ( Input.Down( "Backward" ) ) verticalStrength -= 1f;
		if ( Input.Down( "Right" ) ) horizontalStrenght -= 1f;
		if ( Input.Down( "Left" ) ) horizontalStrenght += 1f;
		if ( Input.Down( "RamLeft" ) ) sharpInputDir += 1f;   // +1 = links (gleiche Konvention wie Left)
		if ( Input.Down( "RamRight" ) ) sharpInputDir -= 1f;   // -1 = rechts (gleiche Konvention wie Right)

		bool sharpPressed = MathF.Abs( sharpInputDir ) > 0.01f;
		bool lateralPressed = MathF.Abs( horizontalStrenght ) > 0.01f;

		bool ramAttempt = sharpPressed && !lateralPressed;
		bool sharpSteering = sharpPressed && lateralPressed;

		if ( ramAttempt )
		{
			// Q/E ohne Left/Right = Ram-Versuch, kein Lenkinput
			horizontalStrenght = 0f;
		}
		else if ( sharpSteering )
		{
			// Left+Q oder Right+E = scharf einlenken (Lenken wird in Richtung des sharp-Inputs verstärkt)
			horizontalStrenght += sharpInputDir * SharpSteerMultiplier;
		}

		if ( IsDrunk )
		{
			verticalStrength = -verticalStrength;
			horizontalStrenght = -horizontalStrenght;
			sharpInputDir = -sharpInputDir;
		}

		moveInput = new Vector2( verticalStrength, horizontalStrenght );
		IsSharpSteering = sharpSteering;
		IsRamAttempting = ramAttempt;
		RamDirection = sharpInputDir;
	}

	private void TryApplyLurch()
	{
		if ( !Input.Down( "Forward" ) ) return;
		if ( Input.Down( "Left" ) || Input.Down( "Right" ) ) return;

		bool leftPressed = Input.Pressed( "RamLeft" );
		bool rightPressed = Input.Pressed( "RamRight" );

		if ( leftPressed == rightPressed ) return;

		float dir = leftPressed ? 1f : -1f;
		if ( IsDrunk ) dir = -dir;

		Vector3 right = WorldRotation.Right;
		Vector3 impulse = -right * dir * LurchImpulse * Rigidbody.Mass;
		Vector3 frontWorld = WorldPosition + WorldRotation.Forward * LurchForwardOffset;
		Rigidbody.ApplyImpulseAt( frontWorld, impulse );
	}

	private void ApplyLocomotion( float acceleration )
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

	private void ApplySteering( float torqueInput )
	{
		if ( MathF.Abs( torqueInput ) < SteerInputDeadzone )
		{
			if ( SteerReleaseDamping > 0f )
			{
				Vector3 av = Rigidbody.AngularVelocity;
				float kill = 1f - MathF.Exp( -SteerReleaseDamping * Time.Delta );
				av.z -= av.z * kill;
				Rigidbody.AngularVelocity = av;
			}
			return;
		}

		if ( MathF.Abs( Rigidbody.AngularVelocity.z ) >= MaxAngularSpeed ) return;

		float mass = Rigidbody.Mass;
		Rigidbody.ApplyTorque( Vector3.Up * SteerTorque * torqueInput * mass );
	}
}
