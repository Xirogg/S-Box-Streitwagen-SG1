using Sandbox;
using LapSystem;
using System;

public sealed class TestControlls : Component
{
	[Property, Group( "Speed" )] public float PullForce { get; set; } = 5f;
	[Property, Group( "Speed" )] public float BrakeForce { get; set; } = 5f;

	[Property, Group( "Speed" )] public float MaxVerticalSpeed { get; set; } = 1500f;

	[Property, Group( "Steering" )] public float SteerTorque { get; set; } = 10f;
	[Property, Group( "Steering" )] public float MaxAngularSpeed { get; set; } = 10f;
	[Property, Group( "Steering" )] public float LateralGrip { get; set; } = 4f;
	[Property, Group( "Steering" )] public float SharpSteerMultiplier { get; set; } = 1.4f;
	[Property, Group( "Steering" )] public float SteerReleaseDamping { get; set; } = 12f;

	private const float SteerInputDeadzone = 0.01f;

	[Property, Group( "Ram Lurch" )] public float LurchSpeed { get; set; } = 700f;
	[Property, Group( "Ram Lurch" )] public float LurchDuration { get; set; } = 0.25f;
	[Property, Group( "Ram Lurch" ), Range( 0f, 2f )] public float ChariotLurchScale { get; set; } = 1f;
	[Property, Group( "Ram Lurch" )] public float LurchYawImpulse { get; set; } = 0f;
	[Property, Group( "Ram Lurch" )] public float LurchForwardBoost { get; set; } = 0f;

	[Property, Group( "GameObjects" )] public Rigidbody Rigidbody { get; set; }
	[Property, Group( "GameObjects" )] public Rigidbody ChariotRigidbody { get; set; }

	[Sync] private Vector2 moveInput { get; set; }

	[Sync, Property, Group( "Identity" )] public Guid PlayerId { get; set; }

	// --- Drunk Drive Timer ---------------------------------------------------------
	//
	// Single source of truth for drunk-driving state. The timer is owner-authoritative
	// and synced to all peers, so anyone (VFX, UI) can read IsDrunk without going
	// through this script directly.
	//
	// AddDrunkTime is [Rpc.Owner] so any peer (e.g. the Dionysos caster) can stack
	// drunk time onto a remote player — the call routes to that player's owning peer,
	// which mutates the synced state.

	/// <summary>Seconds of drunk-driving left. Ticks down on the owning peer.</summary>
	[Sync] public float DrunkDriveTimer { get; private set; }

	/// <summary>True while <see cref="DrunkDriveTimer"/> &gt; 0.</summary>
	[Sync] public bool IsDrunk { get; private set; }

	private bool _lastObservedDrunk;

	/// <summary>
	/// Stack drunk time onto this player. Routed to the owning peer so the timer
	/// ticks authoritatively and stacking from multiple casters Just Works.
	/// </summary>
	[Rpc.Owner]
	public void AddDrunkTime( float seconds )
	{
		if ( seconds <= 0f ) return;
		DrunkDriveTimer += seconds;
		if ( !IsDrunk ) IsDrunk = true;
	}

	private void TickDrunkTimer()
	{
		if ( DrunkDriveTimer <= 0f )
		{
			if ( IsDrunk ) IsDrunk = false;
			return;
		}

		DrunkDriveTimer = MathF.Max( 0f, DrunkDriveTimer - Time.Delta );
		if ( DrunkDriveTimer <= 0f )
			IsDrunk = false;
	}

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

	private float _lurchUntil = -999f;
	private Vector3 _lurchDir;

	private bool LurchActive => Time.Now < _lurchUntil;

	protected override void OnStart()
	{
		_lastObservedDrunk = IsDrunk;

		if ( IsProxy ) return;

		if ( PlayerId == Guid.Empty )
			PlayerId = Guid.NewGuid();
	}

	protected override void OnUpdate()
	{
		// Drunk transition observer runs on all peers so the visual filter hooked to
		// OnDrunkChanged fires for proxies too.
		bool drunkNow = IsDrunk;
		if ( drunkNow != _lastObservedDrunk )
		{
			_lastObservedDrunk = drunkNow;
			OnDrunkChanged?.Invoke( drunkNow );
		}

		if ( IsProxy ) return;

		if ( RaceManager.Instance?.StartCountdownTimeLeft > 0f )
		{
			moveInput = Vector2.Zero;
			return;
		}

		ApplyInputs();
		TryApplyLurch();
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		TickDrunkTimer();

		ApplyHorseLateralGrip();
		ApplyLocomotion( moveInput.x );
		ApplySteering( moveInput.y );
		MaintainLurch();
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

		// Q (dir=+1) swings left, E (dir=-1) swings right. Locked to the horses' right
		// vector so both bodies use one shared sideways direction.
		Vector3 swing = -WorldRotation.Right * dir;
		swing.z = 0f;
		if ( swing.LengthSquared < 0.0001f ) return;
		swing = swing.Normal;

		// Open a short window. MaintainLurch keeps re-injecting the swing velocity every
		// fixed tick and ApplyHorseLateralGrip backs off while the window is open, so the
		// horses can't bleed off the swing faster than the chariot. End result: both bodies
		// translate in lockstep regardless of mass, grip, or joint reaction.
		_lurchDir = swing;
		_lurchUntil = Time.Now + LurchDuration;

		Vector3 forwardBoost = WorldRotation.Forward * LurchForwardBoost;
		ForceSetLateralVelocity( Rigidbody, swing, LurchSpeed, forwardBoost );
		if ( ChariotRigidbody is not null )
			ForceSetLateralVelocity( ChariotRigidbody, swing, LurchSpeed * ChariotLurchScale, forwardBoost );

		if ( LurchYawImpulse != 0f )
		{
			Vector3 yawKick = Vector3.Up * (LurchYawImpulse * dir);
			Rigidbody.AngularVelocity += yawKick;
			if ( ChariotRigidbody is not null )
				ChariotRigidbody.AngularVelocity += yawKick;
		}
	}

	private void MaintainLurch()
	{
		if ( !LurchActive ) return;
		if ( Rigidbody is null ) return;

		// Keep both bodies pinned to the target lateral speed for the whole window so
		// nothing (grip, joint, drift impulse) can pull them out of sync mid-swing.
		ForceSetLateralVelocity( Rigidbody, _lurchDir, LurchSpeed, Vector3.Zero );
		if ( ChariotRigidbody is not null )
			ForceSetLateralVelocity( ChariotRigidbody, _lurchDir, LurchSpeed * ChariotLurchScale, Vector3.Zero );
	}

	private static void ForceSetLateralVelocity( Rigidbody body, Vector3 swingDir, float swingSpeed, Vector3 extra )
	{
		// Replace the component of velocity along swingDir with exactly swingSpeed, keep
		// the rest (forward, vertical) untouched.
		Vector3 vel = body.Velocity;
		float currentAlong = Vector3.Dot( vel, swingDir );
		vel += swingDir * (swingSpeed - currentAlong);
		body.Velocity = vel + extra;
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
		if ( LurchActive ) return;

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
