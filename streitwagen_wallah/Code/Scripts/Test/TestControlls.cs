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

	/// <summary>
	/// Multiplies <see cref="SteerTorque"/> at very low forward speed (e.g. climbing
	/// a steep hill) and tapers back to 1.0 by the time the player reaches
	/// <see cref="SteerBoostFullSpeed"/>. Compensates for the friction that the
	/// pitch/roll-locked box collider drags along the slope when it rests on
	/// one edge — without this, turning while crawling uphill feels dead.
	///
	/// Keep this modest (≈1.5). Higher values combined with the very low yaw
	/// inertia at standstill make the horse spin out in place when the player
	/// just taps a direction without moving.
	/// </summary>
	[Property, Group( "Steering" )] public float LowSpeedSteerBoost { get; set; } = 1.5f;
	[Property, Group( "Steering" )] public float SteerBoostFullSpeed { get; set; } = 400f;

	private const float SteerInputDeadzone = 0.01f;

	[Property, Group( "Ram Lurch" )] public float LurchImpulse { get; set; } = 800f;
	//[Property, Group( "Ram Lurch" )]
	public float LurchForwardOffset { get; set; } = 60f;

	/// <summary>
	/// How far below the horse to probe for ground when redirecting the pull
	/// force along the slope. If the raycast misses (the horse is genuinely
	/// airborne), pull falls back to plain horizontal.
	/// </summary>
	[Property, Group( "Terrain" )] public float GroundProbeDistance { get; set; } = 60f;

	/// <summary>
	/// Downward force (scaled by mass) applied while the body is in the small
	/// gap above the terrain. Keeps the box collider in contact when going
	/// over slope edges so friction-based steering keeps working. Set to 0
	/// to disable. Don't set this very high — it will fight legitimate jumps
	/// over bumps.
	/// </summary>
	[Property, Group( "Terrain" )] public float GroundStickStrength { get; set; } = 600f;

	/// <summary>
	/// Vertical gap (in cm) above the ground in which the stick force applies.
	/// Inside this gap → pull down. Outside → assume genuinely airborne.
	/// </summary>
	[Property, Group( "Terrain" )] public float GroundStickGap { get; set; } = 30f;

	/// <summary>
	/// How much of the slope-tangent component of gravity is cancelled while
	/// the horse is on the ground. 0 = vanilla physics (cart slides toward the
	/// fall line of every hill), 1 = full cancellation (the slope no longer
	/// pulls the cart sideways at all, so steering input is the only thing
	/// that decides which way the cart goes — exactly the "elevation doesn't
	/// affect steering" feeling the player asked for).
	///
	/// Only the *tangent* part of gravity is cancelled; the normal part still
	/// presses the cart into the ground so contact and friction stay normal.
	/// </summary>
	[Property, Group( "Terrain" ), Range( 0f, 1f )] public float SlopeGripStrength { get; set; } = 1f;

	[Property, Group( "GameObjects" )] public Rigidbody Rigidbody { get; set; }

	private Rigidbody _chariotBody;

	/// <summary>
	/// The chariot body this horse pair pulls. Found by matching the ChariotPhysics whose
	/// HorsePairRb is this horse, then cached. Needed because the hitch is now an articulated
	/// BallJoint (not a rigid HingeJoint), so the lurch has to push the cart itself — shoving
	/// only the horse no longer drags the cart sideways with it.
	/// </summary>
	private Rigidbody ChariotBody
	{
		get
		{
			if ( _chariotBody.IsValid() ) return _chariotBody;
			foreach ( var cp in Scene.GetAllComponents<ChariotPhysics>() )
			{
				if ( cp.HorsePairRb == Rigidbody )
				{
					_chariotBody = cp.Body;
					break;
				}
			}
			return _chariotBody;
		}
	}

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


		CancelSlopeGravity();
		ApplyHorseLateralGrip();
		ApplyLocomotion( moveInput.x );
		ApplySteering( moveInput.y );
		StickToGround();
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

		Vector3 lurchDir = -WorldRotation.Right * dir;

		// Horse: shove its front sideways (translates + yaws it into the dodge).
		Vector3 frontWorld = WorldPosition + WorldRotation.Forward * LurchForwardOffset;
		Rigidbody.ApplyImpulseAt( frontWorld, lurchDir * LurchImpulse * Rigidbody.Mass );

		// Chariot: with the articulated ball-joint hitch the cart is no longer rigidly
		// bolted to the horse, so the shove above doesn't drag it sideways on its own.
		// Give the cart the SAME sideways delta-v (impulse = mass × LurchImpulse) at its
		// centre of mass, so horse and cart dodge together — no need to crank LurchImpulse
		// high enough to launch the horses.
		var chariot = ChariotBody;
		if ( chariot.IsValid() )
			chariot.ApplyImpulse( lurchDir * LurchImpulse * chariot.Mass );
	}

	private void ApplyLocomotion( float acceleration )
	{
		if ( acceleration == 0f ) return;

		// Start from the body's facing direction in the horizontal plane.
		Vector3 forward = WorldRotation.Forward.WithZ( 0f );
		if ( forward.LengthSquared < 0.0001f ) return;
		forward = forward.Normal;

		// Redirect the pull force along the slope surface beneath the horse.
		// Reason: with pitch/roll locked, the box collider stays level and
		// rams into slope edges with a horizontal force. The contact normal
		// then bounces the body upward and the player goes briefly airborne,
		// losing steering until they land. Projecting forward onto the local
		// ground plane sends the force *along* the slope instead of into it,
		// so the body climbs smoothly without launching.
		Vector3 normal = ProbeGroundNormal();
		Vector3 surfaceForward = forward - normal * Vector3.Dot( forward, normal );
		if ( surfaceForward.LengthSquared < 0.0001f ) surfaceForward = forward;
		else surfaceForward = surfaceForward.Normal;

		// MaxVerticalSpeed gates are measured against horizontal motion in the
		// player's facing direction — same as before, but using the horizontal
		// (not surface-projected) forward so the cap stays consistent across
		// flat and sloped ground.
		Vector3 planarVel = Rigidbody.Velocity.WithZ( 0f );
		float forwardSpeed = Vector3.Dot( planarVel, forward );
		float planarSpeed = planarVel.Length;

		if ( acceleration > 0 && planarSpeed >= MaxVerticalSpeed && forwardSpeed > 0 ) return;
		if ( acceleration < 0 && forwardSpeed <= -MaxVerticalSpeed ) return;

		float mass = Rigidbody.Mass;
		Rigidbody.ApplyForce( surfaceForward * PullForce * acceleration * mass );
	}

	/// <summary>
	/// Casts a ray straight down from the body to find the ground normal.
	/// Used to align the pull force with the slope. Returns world up when no
	/// ground is found within <see cref="GroundProbeDistance"/> (treats the
	/// body as airborne and lets the regular horizontal pull apply).
	/// </summary>
	private Vector3 ProbeGroundNormal()
	{
		Vector3 from = WorldPosition;
		Vector3 to = from + Vector3.Down * GroundProbeDistance;
		var tr = Scene.Trace
			.Ray( from, to )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();
		return tr.Hit ? tr.Normal : Vector3.Up;
	}

	/// <summary>
	/// Cancels the component of gravity that runs along the slope surface, so
	/// the horse doesn't naturally slide downhill while the player is trying
	/// to steer across or up a hill. Without this, the slope acts as a
	/// constant sideways force fighting steering input — which is the "I
	/// can't steer left because the hill wants me to go right" feeling.
	///
	/// Only fires when grounded (raycast hit). Airborne → gravity normal,
	/// so jumps and ramps still behave correctly.
	/// </summary>
	private void CancelSlopeGravity()
	{
		if ( SlopeGripStrength <= 0f ) return;

		Vector3 from = WorldPosition;
		Vector3 to = from + Vector3.Down * GroundProbeDistance;
		var tr = Scene.Trace
			.Ray( from, to )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		if ( !tr.Hit ) return;

		// Gravity vector (acceleration units) from the physics world — works
		// even if the project changes scene gravity later.
		Vector3 gravity = Scene.PhysicsWorld.Gravity;
		Vector3 normal = tr.Normal;

		// Split gravity into normal + tangent components, keep only the tangent.
		// That's the part that pulls the cart along the slope surface.
		Vector3 gravityTangent = gravity - normal * Vector3.Dot( gravity, normal );

		// Apply opposing force. ApplyForce takes Newtons, so multiply by mass
		// to translate from acceleration units back to force.
		Rigidbody.ApplyForce( -gravityTangent * Rigidbody.Mass * SlopeGripStrength );
	}

	/// <summary>
	/// Applies a small downward force when the horse is in the gap just above
	/// the terrain. Prevents the brief "launch" off a slope edge that strips
	/// friction and kills steering. Disabled at <see cref="GroundStickStrength"/>
	/// = 0 so the player can still jump over genuine bumps.
	/// </summary>
	private void StickToGround()
	{
		if ( GroundStickStrength <= 0f ) return;

		Vector3 from = WorldPosition;
		Vector3 to = from + Vector3.Down * GroundProbeDistance;
		var tr = Scene.Trace
			.Ray( from, to )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		if ( !tr.Hit ) return;
		if ( tr.Distance > GroundStickGap ) return; // genuinely airborne — let gravity handle it
		if ( Rigidbody.Velocity.z <= 0f ) return;    // already falling, no need to pull harder

		Rigidbody.ApplyForce( Vector3.Down * GroundStickStrength * Rigidbody.Mass );
	}

	private void ApplyHorseLateralGrip()
	{
		if ( LateralGrip <= 0f ) return;

		// Project Right onto the horizontal plane. On a slope the body's local
		// Right is tilted, so dotting velocity against it picks up part of the
		// vertical (gravity) component and the velocity subtraction cancels
		// gravity — which made the horse appear to float on inclines.
		Vector3 right = WorldRotation.Right.WithZ( 0f );
		if ( right.LengthSquared < 0.0001f ) return;
		right = right.Normal;

		float lateral = Vector3.Dot( Rigidbody.Velocity, right );
		float kill = 1f - MathF.Exp( -LateralGrip * Time.Delta );
		Rigidbody.Velocity -= right * (lateral * kill);
	}

	private void ApplySteering( float torqueInput )
	{
		// Steer around the body's own Up axis. With pitch/roll locked on the
		// rigidbody this is identical to world Up, but the projection keeps the
		// code correct if those locks are ever removed.
		Vector3 yawAxis = WorldRotation.Up;
		float yawRate = Vector3.Dot( Rigidbody.AngularVelocity, yawAxis );

		if ( MathF.Abs( torqueInput ) < SteerInputDeadzone )
		{
			if ( SteerReleaseDamping > 0f )
			{
				float kill = 1f - MathF.Exp( -SteerReleaseDamping * Time.Delta );
				Rigidbody.AngularVelocity -= yawAxis * (yawRate * kill);
			}
			return;
		}

		// Counter-steer fix: previously this returned whenever |yawRate| hit the
		// cap, which blocked every torque — including the one that would slow
		// or reverse the current spin. Only block torque that would push the
		// spin further past the cap in the same direction it's already going.
		bool pushingSameWay = MathF.Sign( yawRate ) == MathF.Sign( torqueInput );
		if ( pushingSameWay && MathF.Abs( yawRate ) >= MaxAngularSpeed ) return;

		// Boost steering torque at low forward speed. Climbing a hill, the
		// box collider rests on its downhill edge and yaw torque gets eaten
		// by friction against the slope. Without this scaling, the player
		// can get "stranded" mid-turn on an incline.
		float planarSpeed = Rigidbody.Velocity.WithZ( 0f ).Length;
		float speedFactor = MathX.Clamp( planarSpeed / MathF.Max( SteerBoostFullSpeed, 1f ), 0f, 1f );
		float boost = MathX.Lerp( LowSpeedSteerBoost, 1f, speedFactor );

		float mass = Rigidbody.Mass;
		Rigidbody.ApplyTorque( yawAxis * SteerTorque * torqueInput * mass * boost );
	}
}
