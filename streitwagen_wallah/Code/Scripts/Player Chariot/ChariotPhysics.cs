using Sandbox;
using System;
using Sandbox.Physics;


public sealed class ChariotPhysics : Component, Component.ICollisionListener
{
	[Property, Group( "Joint Configuration" )]
	public Rigidbody HorsePairRb { get; set; }

	[Property, Group("Joint Configuration")]
	public Joint _joint; 

	[Property, Group( "Joint Configuration" )]
	public float YawLimit { get; set; } = 45f;

	[Property, Group( "Joint Configuration" )]
	public float YawSpring { get; set; } = 50f;

	[Property, Group( "Joint Configuration" )]
	public float YawDamper { get; set; } = 10f;

	[Property, Group( "Stability" )]
	public float Downforce { get; set; } = 8000f;   // 
	[Property, Group( "Stability" )]
	public float AntiFlipTorque { get; set; } = 20000f;

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = true;

	public float CurrentSpeed { get; private set; }
	public float DriftAngle { get; private set; }

		private Rigidbody _rb;
	
	private float _debugTimer;
	

	/// <summary>
	/// Referenz auf das Pferde-Rigidbody dieses Spielers — wird für Ram-Checks gebraucht,
	/// damit der andere Spieler erkennen kann zu wem der getroffene Wagen gehört.
	/// </summary>
	public Rigidbody HorsePairRigidbody => HorsePairRb;

	protected override void OnAwake()
	{
		InitRigidbody();
	}

	private void InitRigidbody()
	{
		_rb ??= GetComponent<Rigidbody>();
	}

	/// <summary>
	/// Nach dem Positionieren beider Bodies aufrufen, um den HingeJoint zu konfigurieren.
	/// Anker werden aus den tatsächlichen Weltpositionen berechnet, sodass sie immer passen.
	/// </summary>


	// === ICollisionListener ===

	public void OnCollisionStart( Collision collision )
	{
		// Wagen-Kollision an die PlayerCollisions des eigenen Spielers weiterleiten,
		// damit auch ein Wagen-gegen-Wagen oder Wagen-gegen-Pferde Treffer als Ram gewertet wird.
		EnsureOwnerCollisions();
		
	}

	public void OnCollisionUpdate( Collision collision )
	{
		// Während der Kontakt anhält, an PlayerCollisions weitergeben — wird für die
		// Ram-Akkumulation (Q/E ohne A/D, 2 Sekunden Kontakt) gebraucht.
		EnsureOwnerCollisions();
		
	}

	public void OnCollisionStop( CollisionStop collision )
	{
		// Optional: hier könntest du z.B. Ram-Akkumulator-State zurücksetzen
	}

	private void EnsureOwnerCollisions()
	{
		// PlaceHolder — die ursprüngliche Unity-Implementierung war hier auch leer.
		// Wenn deine PlayerCollisions z.B. am Pferde-GameObject hängt, würdest du hier
		// einmalig den Component holen, etwa:
		//   if ( _ownerCollisions == null && HorsePairRb != null )
		//       _ownerCollisions = HorsePairRb.Components.Get<PlayerCollisions>();

	}

	protected override void OnFixedUpdate()
	{
		if ( _rb == null ) InitRigidbody();
		if ( _rb == null ) return;

		ApplyDownforce();
		ApplyAntiFlip();
		UpdateTelemetry();

		if ( DebugLog )
		{
			_debugTimer += Time.Delta;
			if ( _debugTimer >= 0.5f )
			{
				_debugTimer = 0f;
				Log.Info( $"[Chariot] Speed={CurrentSpeed:F2} u/s | DriftAngle={DriftAngle:F1}° | " +
					$"Pos={WorldPosition} | Vel={_rb.Velocity}" );
			}
		}
	}

	private void ApplyDownforce()
	{
		// In s&box zeigt Down nach -Z (nicht -Y wie in Unity).
		_rb.ApplyForce( Vector3.Down * Downforce );
	}

	private void ApplyAntiFlip()
	{
		// In s&box bekommen wir Pitch / Yaw / Roll direkt aus der Rotation.
		Angles angles = WorldRotation.Angles();
		float tiltPitch = NormalizeAngle( angles.pitch );
		float tiltRoll = NormalizeAngle( angles.roll );

		Vector3 correctionTorque = Vector3.Zero;
		// Drehmoment um die lokale Rechts-Achse, um Pitch zu korrigieren
		correctionTorque += WorldRotation.Right * (-tiltPitch * AntiFlipTorque * Time.Delta);
		// Drehmoment um die lokale Forward-Achse, um Roll zu korrigieren
		correctionTorque += WorldRotation.Forward * (-tiltRoll * AntiFlipTorque * Time.Delta);

		_rb.PhysicsBody.ApplyTorque( correctionTorque );
	}

	private void UpdateTelemetry()
	{
		CurrentSpeed = _rb.Velocity.Length;

		if ( CurrentSpeed > 0.5f )
		{
			DriftAngle = Vector3.GetAngle( _rb.Velocity, WorldRotation.Forward );
		}
		else
		{
			DriftAngle = 0f;
		}
	}

	private static float NormalizeAngle( float angle )
	{
		while ( angle > 180f ) angle -= 360f;
		while ( angle < -180f ) angle += 360f;
		return angle;
	}
 
}
