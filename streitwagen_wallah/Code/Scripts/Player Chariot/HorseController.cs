using Sandbox;
using System; 

public sealed class HorseController : Component
{
	[Property, Group( "Player" )]
	public int PlayerIndex { get; set; } = 0;

	[Property, Group( "Movement" )]
	public float PullForce { get; set; } = 120000f;

	[Property, Group( "Movement" )]
	public float MaxSpeed { get; set; } = 480f; // ~12 m/s in inch/s

	[Property, Group( "Movement" )]
	public float BrakeForce { get; set; } = 80000f;

	[Property, Group( "Steering" )]
	public float SteerTorque { get; set; } = 32000f;

	[Property, Group( "Steering" )]
	public float MaxAngularVelocity { get; set; } = 2f;

	[Property, Group( "Steering" )]
	public float SharpSteerMultiplier { get; set; } = 1.4f;

	[Property, Group( "Stability" )]
	public float LateralDampingForce { get; set; } = 20000f;

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = false;

	[Property, Group("RigidBody") ]
	public Rigidbody _rb {get; set;} 
	private Vector2 _moveInput;
	private float _speedMultiplier = 1f;
	private float _debugTimer;

		/// <summary>
	/// True solange der Spieler A/D (bzw. Pfeile) UND zusätzlich Q/E (bzw. K/L) drückt.
	/// Das ist der "scharf einlenken"-Modus — Q/E verstärkt das Lenken nur dann.
	/// </summary>
	public bool IsSharpSteering { get; private set; }

	/// <summary>
	/// True wenn der Spieler Q/E (bzw. K/L) drückt OHNE gleichzeitig A/D (bzw. Pfeile links/rechts) zu halten.
	/// In diesem Fall wird nicht gelenkt — stattdessen versucht der Spieler einen Wagen zu rammen
	/// (siehe PlayerCollisions).
	/// </summary>
	public bool IsRamAttempting { get; private set; }

	/// <summary>
	/// Vorzeichen der Q/E (bzw. K/L) Eingabe: -1 = links (Q/K), +1 = rechts (E/L), 0 = keine.
	/// </summary>
	public float RamDirection { get; private set; }

	public static bool MovementEnabled { get; set; } = true;

	public void SetSpeedMultiplier( float multiplier )
	{
		_speedMultiplier = multiplier;
	}


	protected override void OnAwake() {
		_rb ??= GetComponent<Rigidbody>(); 
		Log.Info(_rb); 
		Log.Info("Test"); 
	
		
	}

	protected override void OnFixedUpdate()
	{
		ApplyBaseMovement(); 
	}



	

	private void ApplySteering( float steerInput )
	{
		if ( MathF.Abs( steerInput ) < 0.01f ) return;

		// In s&box ist ApplyTorque (im Gegensatz zu ApplyForce) nur über PhysicsBody erreichbar.
		// Up-Achse in s&box ist Vector3.Up (+Z).
		_rb.PhysicsBody.ApplyTorque( Vector3.Up * SteerTorque * steerInput );
	}

	private void ApplyLocomotion( float throttle )
	{
		Vector3 forward = LocalRotation.Forward;
		float currentForwardSpeed = Vector3.Dot( _rb.Velocity, forward );
		Log.Info(forward + throttle + currentForwardSpeed);
		if ( throttle > 0f )
		{
			if ( currentForwardSpeed < MaxSpeed * _speedMultiplier )
			{
				 
				_rb.ApplyForce( forward * PullForce * throttle );
				
			}
		}
		else if ( throttle < 0f )
		{
			_rb.ApplyForce( forward * BrakeForce * throttle );
		}
	}

	


	private void ApplyBaseMovement() 
	{	
		float horizontal = 0f;
		float vertical = 0f;

		if ( !MovementEnabled )
		{
			_moveInput = Vector2.Zero;
			IsSharpSteering = false;
			IsRamAttempting = false;
			RamDirection = 0f;
			return;
		}

		float lateralInput = 0f;     // A/D bzw. Pfeile links/rechts
		float sharpInputDir = 0f;    // Q/E bzw. K/L, -1 links / +1 rechts

		

		if ( Input.Down("Forward"))  vertical += 1f;
		if ( Input.Down( "Backward" ) ) vertical -= 1f;
		if ( Input.Down( "Leftt" ) ) lateralInput -= 1f;
		if ( Input.Down( "Right" ) ) lateralInput += 1f;
		//if ( Input.Down( "sharp_left" ) ) sharpInputDir -= 1f;
		//if ( Input.Down( "sharp_right" ) ) sharpInputDir += 1f;
		

		bool sharpPressed = Math.Abs( sharpInputDir ) > 0.01f;
		bool lateralPressed = MathF.Abs( lateralInput ) > 0.01f;

	

		// Q/E ohne A/D = Ram-Versuch, kein Lenken
		bool ramAttempt = sharpPressed && !lateralPressed;

		if ( ramAttempt )
		{
			horizontal = 0f;
		}
		else if ( sharpPressed && lateralPressed )
		{
			// beides zusammen = scharf einlenken (verstärkt in Richtung des sharp-Inputs)
			horizontal = lateralInput + sharpInputDir * SharpSteerMultiplier;
		}
		else
		{
			horizontal = lateralInput;
		}

		_moveInput = new Vector2( horizontal, vertical );
		IsSharpSteering = sharpPressed && lateralPressed;
		IsRamAttempting = ramAttempt;
		RamDirection = sharpInputDir;
		Log.Info(_moveInput); 

		ApplyLocomotion( _moveInput.y );
		ApplySteering( _moveInput.x );
	}

}
