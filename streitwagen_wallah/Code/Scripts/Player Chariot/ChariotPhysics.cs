using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>
/// Passiver Streitwagen-Körper. Wird vom HorsePair via BallJoint (Deichsel/Hitch) gezogen.
/// Der Joint sitzt auf einem auto-erzeugten Pivot-Child am HitchPoint und wirkt wie ein
/// echtes Deichsel-Gelenk: Yaw (Lenken) und Pitch (Höhenunterschied über Hügel) sind frei
/// innerhalb von Limits, Roll ist gesperrt. Dadurch können Pferd und Wagen auf Steigungen
/// unterschiedlich hoch stehen, ohne dass sich die beiden Bodies ineinanderziehen.
/// </summary>
public sealed class ChariotPhysics : Component, Component.ICollisionListener, ISpeedModifiable
{
	[Property, Group( "Joint" )] public Rigidbody HorsePairRb { get; set; }
	[Property, Group( "Joint" )] public GameObject HitchPoint { get; set; }
	/// <summary>
	/// How far the chariot may yaw (steer) relative to the horse, in degrees. Within this
	/// the chariot swings freely behind the horse and hard-stops at the limit — same
	/// character as the old HingeJoint. Note: the ball hitch uses a single swing cone, so
	/// the effective limit is max(YawLimit, PitchLimit). Keeping them close avoids surprises.
	/// </summary>
	[Property, Group( "Joint" )] public float YawLimit { get; set; } = 170f;
	/// <summary>
	/// How far the chariot may pitch (nose up/down) relative to the horse, in degrees. This
	/// is what lets the horse and chariot ride at different heights over a hill: on a climb
	/// the cart's rear stays on the lower ground and the body tilts nose-up at the hitch
	/// instead of being dragged to the horse's height. Bigger = more vertical articulation;
	/// too big lets the cart fold under the horse on steep terrain. Shares the swing cone
	/// with <see cref="YawLimit"/> (the larger of the two wins).
	/// </summary>
	[Property, Group( "Joint" )] public float PitchLimit { get; set; } = 45f;
	/// <summary>
	/// How far the chariot may roll (twist along the draw-pole) relative to the horse, in
	/// degrees. Kept near-zero so the cart can't barrel-roll sideways relative to the horse;
	/// a couple of degrees of give avoids a perfectly rigid twist constraint fighting the solver.
	/// </summary>
	[Property, Group( "Joint" )] public float RollLimit { get; set; } = 2f;

	[Property, Group( "Movement" )] public float MaxSpeed { get; set; } = 2500f;

	private readonly Dictionary<string, float> _speedModifiers = new();
	private float _speedMultiplier = 1f;

	public void SetSpeedMultiplier( string key, float multiplier )
	{
		_speedModifiers[key] = multiplier;
		RecomputeSpeedMultiplier();
	}

	public void ClearSpeedMultiplier( string key )
	{
		if ( _speedModifiers.Remove( key ) )
			RecomputeSpeedMultiplier();
	}

	private void RecomputeSpeedMultiplier()
	{
		float m = 1f;
		foreach ( var v in _speedModifiers.Values )
			m *= v;
		_speedMultiplier = m;
	}

	public float EffectiveMaxSpeed => MaxSpeed * _speedMultiplier;

	[Property, Group( "Stability" ), Range( 0f, 30f )] public float LateralGrip { get; set; } = 0f;

	/// <summary>
	/// Cancels the part of gravity that runs along the slope surface beneath
	/// the chariot. Matches the same property on the horse — without it, even
	/// if the horse holds its position on a hill, the chariot's own mass still
	/// slides downhill and yanks the horse via the hitch joint. 1 = full
	/// cancellation (slope no longer pulls the chariot sideways), 0 = vanilla.
	/// </summary>
	[Property, Group( "Stability" ), Range( 0f, 1f )] public float SlopeGripStrength { get; set; } = 1f;
	[Property, Group( "Stability" )] public float GroundProbeDistance { get; set; } = 60f;

	[Property, Group( "Drift" )] public float DriftForce { get; set; } = 24000f;
	[Property, Group( "Drift" )] public float DriftRearOffset { get; set; } = 100f;
	[Property, Group( "Drift" )] public float DriftMinSpeed { get; set; } = 15f;
	[Property, Group( "Drift" )] public float DriftFullSpeed { get; set; } = 120f;
	public float DriftMaxYawRate { get; set; } = 10f;
	[Property, Group( "Drift" ), Range( 0f, 5f )] public float ChariotAngularDamping { get; set; } = 0.04f;

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = true;
	[Property, Group( "Debug" ), ReadOnly] public float CurrentSpeed { get; private set; }
	[Property, Group( "Debug" ), ReadOnly] public float DriftAngle { get; private set; }
	[Property] public ParticleEmitter DustEmitter_L { get; set; }
	[Property] public ParticleEmitter DustEmitter_R { get; set; }

	[RequireComponent] public Rigidbody Body { get; set; }

	[Sync] private bool DustActive { get; set; }

	private BallJoint _joint;
	private GameObject _jointPivot;
	private float _debugTimer;


	private PlayerCollisions _ownerCollisions; 

	protected override void OnStart()
	{
		Tags.Add( "chariot" );
		if ( HorsePairRb is not null )
			HorsePairRb.Tags.Add( "chariot" );

		if ( IsProxy ) return; 

		if ( HorsePairRb is not null )
		{
			SetupJoint( HorsePairRb );
		}
		else
		{
			Log.Warning( "[ChariotPhysics] HorsePairRb ist nicht zugewiesen — Joint wird nicht erstellt." );
		}
	}

	public event Action<Collision> ImpactStarted;

	void Component.ICollisionListener.OnCollisionStart( Collision other )
	{
		if (IsProxy) return;
		if ( _ownerCollisions is null && HorsePairRb is not null )
			_ownerCollisions = HorsePairRb.Components.Get<PlayerCollisions>();
		_ownerCollisions?.HandleChariotCollision( other );
		ImpactStarted?.Invoke( other );
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision other ) { }
	void Component.ICollisionListener.OnCollisionStop( CollisionStop other ) { }

	public void SetupJoint( Rigidbody horseRb )
	{
		HorsePairRb = horseRb;

		// Pivot sitzt am Deichsel-Joch (HitchPoint), wenn gesetzt — sonst Fallback auf Pferd-Position.
		// Anker am physischen Verbindungspunkt vermeidet Kippmomente beim Ziehen/Lenken.
		Vector3 pivotPos = HitchPoint is not null ? HitchPoint.WorldPosition : horseRb.WorldPosition;

		_jointPivot = new GameObject( true, "ChariotJointPivot" );
		_jointPivot.SetParent( GameObject );
		_jointPivot.WorldPosition = pivotPos;

		// Achsen-Konvention (im Playtest empirisch bestätigt): Die TWIST-Achse des BallJoints
		// ist die lokale X-Achse des Hosts (= dessen Forward) — NICHT lokales Z wie beim Hinge.
		// Twist soll = Roll *entlang der Deichsel* sein (übers TwistLimit gesperrt), der Swing-
		// Kegel = Pitch+Yaw (frei). Also muss lokales X entlang der Deichsel zeigen — und die
		// Wagen-Rotation tut das bereits, deshalb übernehmen wir sie direkt.
		// (Die vorige Version drehte X auf Up → Twist war Yaw und der Swing-Kegel enthielt Roll,
		//  also kippte der Wagen seitlich weg und trudelte. Genau das war im Test zu sehen.)
		_jointPivot.WorldRotation = WorldRotation;

		// Ein BallJoint ist genau das, was ein echtes Deichsel-Gelenk ist: die Deichsel
		// darf links/rechts schwingen (Yaw → Lenken) und hoch/runter (Pitch → Pferd und
		// Wagen auf unterschiedlicher Höhe über Hügeln), aber nicht um ihre eigene Achse
		// drehen (Roll). Der alte HingeJoint ließ nur Yaw zu und koppelte damit die Höhe
		// des Wagens starr an die des Pferdes — auf flachem Boden ok, an Steigungen falsch.
		//
		// Wichtig fürs Spielgefühl: innerhalb des Swing-Kegels ist der Yaw frei und schlägt
		// erst am Limit hart an — genau wie der alte Hinge innerhalb seiner MinAngle/MaxAngle.
		// Wir verändern das Lenken also kaum, sondern fügen nur die Pitch-Freiheit hinzu.
		// (Der frühere BallJoint-Versuch fühlte sich nur deshalb schlecht an, weil er gar
		// keine Begrenzung hatte und dadurch ein völlig freies Drehgelenk war.)
		_joint = _jointPivot.Components.Create<BallJoint>();
		_joint.Body = horseRb.GameObject;

		// Swing ist EIN Kegel um die Twist-Achse — Pitch und Yaw teilen ihn sich (die
		// Engine begrenzt Swing intern über einen einzelnen Winkel). Wir setzen den Kegel
		// auf das Maximum der beiden gewünschten Limits, damit weder Lenken (Yaw) noch
		// Höhenartikulation (Pitch) enger eingeschränkt wird als beabsichtigt.
		float swingCone = MathF.Max( YawLimit, PitchLimit );
		_joint.SwingLimitEnabled = true;
		_joint.SwingLimit = new Vector2( swingCone, swingCone );

		// Twist = Roll entlang der Deichsel. Nahe Null halten, damit der Wagen sich nicht
		// gegenüber dem Pferd seitlich überschlagen kann.
		_joint.TwistLimitEnabled = true;
		_joint.TwistLimit = new Vector2( -RollLimit, RollLimit );

		_joint.EnableCollision = true;

		// WICHTIG: Die Rotations-Locks des Rigidbodies (Pitch/Yaw/Roll) wirken um die WELT-
		// Achsen, nicht um die lokalen Achsen des Wagens. Der alte Wagen sperrte Welt-Pitch
		// UND Welt-Roll → dadurch blieb er in JEDER Blickrichtung waagerecht. Löst man aber
		// nur EINE davon (für die Höhenartikulation), hängt es von der Fahrtrichtung ab, ob
		// "seitlich kippen" auf die noch gesperrte oder die freie Welt-Achse fällt — der
		// Wagen klappt dann je nach Richtung um. (Das war der zweite Bug im Playtest.)
		//
		// Lösung: ALLE Rotations-Locks lösen und die erlaubte Orientierung allein vom BallJoint
		// bestimmen lassen. Dessen Twist/Swing sind im Joint-Frame definiert, das mit dem Wagen
		// mitdreht — also blickrichtungs-unabhängig korrekt. Twist hält den Roll bei ~0, Swing
		// erlaubt Pitch (Gelände) + Yaw (Lenken). Translation bleibt frei (der Joint pinnt sie
		// am Hitch).
		var locking = Body.Locking;
		locking.Pitch = false;
		locking.Yaw = false;
		locking.Roll = false;
		Body.Locking = locking;
	}

	protected override void OnFixedUpdate()
	{
		// Effekte am Rad — auf allen Clients aus dem synchronisierten Flag gelesen
		if ( DustEmitter_L is not null )
			DustEmitter_L.Enabled = DustActive;

		if ( DustEmitter_R is not null )
			DustEmitter_R.Enabled = DustActive;

		if ( IsProxy ) return;

		UpdateTelemetry();
		DustActive = Body.Velocity.Length > 400f;

		CancelSlopeGravity();
		ApplyDriftImpulse();
		ApplyLateralGrip();
		DampenYaw();
		ClampMaxSpeed();


		if ( DebugLog )
		{
			_debugTimer += Time.Delta;
			if ( _debugTimer >= 0.5f )
			{
				_debugTimer = 0f;
				Log.Info( $"[Chariot] Spd={CurrentSpeed:F1} | Drift={DriftAngle:F1}° | Vel={Body.Velocity}" );
			}
		}
	}

	private void ApplyDriftImpulse()
	{
		if ( HorsePairRb is null || DriftForce <= 0f ) return;

		float speed = Body.Velocity.Length;
		if ( speed < DriftMinSpeed ) return;

		// Yaw rate is rotation around the horse's body Up — not world Z — so it
		// stays correct on inclines instead of bleeding into roll/pitch.
		Vector3 horseUp = HorsePairRb.WorldRotation.Up;
		float yawRate = MathX.Clamp( Vector3.Dot( HorsePairRb.AngularVelocity, horseUp ), -DriftMaxYawRate, DriftMaxYawRate );
		if ( MathF.Abs( yawRate ) < 0.05f ) return;

		float speedFactor = MathX.Clamp( (speed - DriftMinSpeed) / MathF.Max( DriftFullSpeed - DriftMinSpeed, 1f ), 0f, 1f );

		// Push the rear sideways opposite to the turn direction → back end kicks out.
		// Right + rear offset projected to horizontal so the drift kick stays
		// in the ground plane and doesn't lift the chariot on slopes.
		Vector3 right = WorldRotation.Right.WithZ( 0f );
		Vector3 forwardFlat = WorldRotation.Forward.WithZ( 0f );
		if ( right.LengthSquared < 0.0001f || forwardFlat.LengthSquared < 0.0001f ) return;
		right = right.Normal;
		forwardFlat = forwardFlat.Normal;

		Vector3 force = right * (-yawRate * DriftForce * speedFactor);
		Vector3 rearWorld = WorldPosition - forwardFlat * DriftRearOffset;

		Body.ApplyForceAt( rearWorld, force );
	}

	private void DampenYaw()
	{
		if ( ChariotAngularDamping <= 0f ) return;

		// Damp only the yaw component around the chariot's local Up. Using world
		// Z would damp the wrong axis when the chariot is pitched on a hill.
		Vector3 yawAxis = WorldRotation.Up;
		float yawRate = Vector3.Dot( Body.AngularVelocity, yawAxis );
		float keep = MathF.Exp( -ChariotAngularDamping * Time.Delta );
		Body.AngularVelocity -= yawAxis * (yawRate * (1f - keep));
	}

	private void ClampMaxSpeed()
	{
		float cap = EffectiveMaxSpeed;
		if ( cap <= 0f ) return;

		// Clamp only the horizontal speed. The old version scaled the whole
		// 3D velocity, which capped gravity-induced falling speed too and
		// kept the chariot hanging in the air on downhill sections.
		Vector3 vel = Body.Velocity;
		Vector3 planar = vel.WithZ( 0f );
		float planarSpeed = planar.Length;
		if ( planarSpeed > cap )
		{
			planar *= cap / planarSpeed;
			Body.Velocity = new Vector3( planar.x, planar.y, vel.z );
		}
	}

	/// <summary>
	/// Cancels gravity's tangent component along the slope under the chariot.
	/// Same trick as <c>TestControlls.CancelSlopeGravity</c>: keep the cart from
	/// sliding sideways on hills so steering input is the only thing that
	/// decides where it goes.
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

		Vector3 gravity = Scene.PhysicsWorld.Gravity;
		Vector3 normal = tr.Normal;
		Vector3 gravityTangent = gravity - normal * Vector3.Dot( gravity, normal );

		Body.ApplyForce( -gravityTangent * Body.Mass * SlopeGripStrength );
	}

	private void ApplyLateralGrip()
	{
		if ( LateralGrip <= 0f ) return;

		// Horizontal right only — see TestControlls.ApplyHorseLateralGrip for
		// the long explanation. Same bug, same fix: a tilted Right axis on
		// slopes makes the grip subtract from the falling velocity and the
		// chariot stops obeying gravity.
		Vector3 right = WorldRotation.Right.WithZ( 0f );
		if ( right.LengthSquared < 0.0001f ) return;
		right = right.Normal;

		float lateralAmount = Vector3.Dot( Body.Velocity, right );
		float killFactor = 1f - MathF.Exp( -LateralGrip * Time.Delta );
		Body.Velocity -= right * (lateralAmount * killFactor);
	}

	private void UpdateTelemetry()
	{
		CurrentSpeed = Body.Velocity.Length;
		DriftAngle = CurrentSpeed > 5f
			? Vector3.GetAngle( Body.Velocity.WithZ( 0f ), WorldRotation.Forward.WithZ( 0f ) )
			: 0f;
	}
}
