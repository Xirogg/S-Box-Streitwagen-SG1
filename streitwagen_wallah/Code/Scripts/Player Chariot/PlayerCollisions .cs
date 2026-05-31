using Sandbox;
using System;

/// <summary>
/// Friendslop-Style Spieler-gegen-Spieler Kollisionen.
/// Wird auf das Pferde-GameObject gelegt (gleiches GameObject wie der HorseController
/// und das Pferde-Rigidbody).
///
/// Empfängt Kollisionen direkt von den Pferden via Component.ICollisionListener und
/// vom eigenen Wagen via HandleChariotCollision (das ChariotPhysics-Skript leitet
/// seine Wagen-Kollisionen hier rein, damit ein Wagen-vs-Pferde oder Wagen-vs-Wagen
/// Treffer auch als Friendslop-Treffer gewertet wird).
///
/// Der Treffer ist absichtlich überzogen: der Gegner soll richtig schön wegfliegen,
/// inkl. einem Hauch Vertikalimpuls, ordentlich Spin und einem dicken Mindestschubs.
/// </summary>
public sealed class PlayerCollisions : Component, Component.ICollisionListener
{
	[Property, Group( "Friendslop Impulse" )]
	public float BaseImpulse { get; set; } = 3500f;

	[Property, Group( "Friendslop Impulse" )]
	public float ImpulsePerClosingSpeed { get; set; } = 1100f;

	[Property, Group( "Friendslop Impulse" )]
	public float MaxImpulse { get; set; } = 18000f;

	[Property, Group( "Direction Mix" ), Range( 0f, 1f )]
	public float LateralBias { get; set; } = 0.55f;

	[Property, Group( "Direction Mix" ), Range( 0f, 1f )]
	public float VerticalLift { get; set; } = 0.35f;

	/// <summary>
	/// Yaw-Velocity-Spike auf das Opfer pro Impuls-Einheit.
	/// s&amp;box hat kein direktes ApplyAngularImpulse — wir addieren stattdessen
	/// auf AngularVelocity. Der Faktor ist deutlich kleiner als in der Unity-Version,
	/// weil hier rad/s direkt verändert werden statt eines Drehimpulses in Nms.
	/// Bei Bedarf hochdrehen bis es schön spinnt.
	/// </summary>
	[Property, Group( "Spin" )]
	public float AngularImpulseFactor { get; set; } = 0.0008f;

	[Property, Group( "Spin" )]
	public float ChariotExtraSpin { get; set; } = 1.6f;

	[Property, Group( "Chariot Coupling" ), Range( 0f, 2f )]
	public float ChariotImpulseRatio { get; set; } = 1.1f;

	[Property, Group( "Activation" )]
	public float MinClosingSpeed { get; set; } = 0.8f;

	[Property, Group( "Activation" )]
	public float Cooldown { get; set; } = 0.2f;

	[Property, Group( "Activation" )]
	public float SharpSteerMultiplier { get; set; } = 1.8f;

	[Property, Group( "Prefab" )]
	public GameObject CollisionDebrisPrefab { get; set; } 
	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = true;

	[RequireComponent] public Rigidbody Body { get; set; }

	private float _lastRamTime = -999f;
	private PlayerStats _attackerStats;

	/// <summary>
	/// PlayerStats sitting somewhere on this player's hierarchy. Resolved lazily on
	/// the first ram and cached, so we don't pay for a tree walk every collision.
	/// </summary>
	private PlayerStats AttackerStats
	{
		get
		{
			if ( _attackerStats.IsValid() ) return _attackerStats;
			var root = GameObject.Root;
			if ( root is null ) return null;
			_attackerStats = root.Components.Get<PlayerStats>( FindMode.EverythingInSelfAndDescendants );
			return _attackerStats;
		}
	}

	// --- ICollisionListener -----------------------------------------------------

	void Component.ICollisionListener.OnCollisionStart( Collision other )
	{
		
		TryRam( other );
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision other ) { }
	void Component.ICollisionListener.OnCollisionStop( CollisionStop other ) { }

	/// <summary>
	/// Wird von ChariotPhysics aufgerufen, wenn der eigene Wagen mit etwas kollidiert.
	/// </summary>
	public void HandleChariotCollision( Collision collision )
	{
		TryRam( collision );
	}

	// --- Ram-Kern ---------------------------------------------------------------

	private void TryRam( Collision collision )
	{
		if ( Body is null ) return;
		if ( Time.Now - _lastRamTime < Cooldown ) return;

		FindOtherPlayerBodies( collision, out Rigidbody otherHorseRb, out Rigidbody otherChariotRb );
		if ( otherHorseRb is null || otherHorseRb == Body ) return;

		// Push-Richtung aus der Kontakt-Normale. Die Normale zeigt vom anderen Body
		// auf uns zu, also wollen wir das umgekehrte für die Schubrichtung.
		Vector3 contactNormal = collision.Contact.Normal;
		Vector3 pushDirRaw = -contactNormal;
		pushDirRaw.z = 0f; // Z-up: nur horizontalen Anteil weiterverwenden
		if ( pushDirRaw.LengthSquared < 0.0001f ) return;
		var prefabInstance = CollisionDebrisPrefab.Clone( WorldPosition );
		Vector3 pushDir = pushDirRaw.Normal;

		Vector3 myVelFlat = Body.Velocity; myVelFlat.z = 0f;
		Vector3 otherVelFlat = otherHorseRb.Velocity; otherVelFlat.z = 0f;

		float myImpact = Vector3.Dot( myVelFlat, pushDir );
		float otherImpact = Vector3.Dot( otherVelFlat, pushDir );
		float closingSpeed = myImpact - otherImpact;

		// Nur der Angreifer schubst — die Gegenseite sieht negative closingSpeed.
		if ( closingSpeed < MinClosingSpeed ) return;
		if ( myImpact < 0.05f ) return;

		Vector3 finalDir = BuildFinalDirection( pushDir );

		// SharpSteer-Multiplikator. Wenn dein HorseController eine bool-Property
		// IsSharpSteering hat, hier einkommentieren:
		float sharpMult = 1f;
		// var hc = Components.Get<HorseController>();
		// if ( hc is not null && hc.IsSharpSteering ) sharpMult = SharpSteerMultiplier;

		float weightMult = AttackerStats is not null ? AttackerStats.WeightMultiplier : 1f;

		float magnitude = (BaseImpulse + ImpulsePerClosingSpeed * closingSpeed) * sharpMult * weightMult;
		magnitude = MathF.Min( magnitude, MaxImpulse );

		float sideSign = ComputeSideSign( pushDir );

		// --- Opfer-Pferde ---
		otherHorseRb.ApplyImpulse( finalDir * magnitude );
		otherHorseRb.AngularVelocity += Vector3.Up * (magnitude * AngularImpulseFactor * sideSign);

		// --- Opfer-Wagen — fliegt stärker und dreht sich wilder ---
		if ( otherChariotRb is not null && ChariotImpulseRatio > 0f )
		{
			float chariotMag = magnitude * ChariotImpulseRatio;
			otherChariotRb.ApplyImpulse( finalDir * chariotMag );
			otherChariotRb.AngularVelocity += Vector3.Up * (chariotMag * AngularImpulseFactor * ChariotExtraSpin * sideSign);
		}

		_lastRamTime = Time.Now;

		AwardRamCurrency( otherHorseRb );

		if ( DebugLog )
		{
			Log.Info( $"[PlayerCollisions {GameObject.Name}] closing={closingSpeed:F2} | sharp={(sharpMult > 1f)} | " +
				$"weightMult={weightMult:F2} | " +
				$"impulse={magnitude:F0} (Pferde) + {magnitude * ChariotImpulseRatio:F0} (Wagen) | " +
				$"dir={finalDir} | sideSign={sideSign}" );
		}
	}

	// --- Currency hook ----------------------------------------------------------

	/// <summary>
	/// PG &amp; Balancing rule "+5 PG für jeden physischen Treffer mit Q/E".
	/// Runs on the local attacker's side (collisions fire on the owning peer);
	/// NotifyRamHit forwards to the host via Rpc.Host. Only awards when Q/E was
	/// actually held (ram attempt or sharp steer) so casual side-rubs don't grind PG.
	/// </summary>
	private void AwardRamCurrency( Rigidbody victimHorseRb )
	{
		if ( victimHorseRb is null ) return;

		var attackerRoot = GameObject.Root;
		var ctrl = attackerRoot?.Components.Get<TestControlls>( FindMode.EverythingInSelfAndDescendants );
		if ( ctrl is null ) return;
		if ( !ctrl.IsRamAttempting && !ctrl.IsSharpSteering ) return;

		// Only the owning peer reports — otherwise every client that simulates
		// the same collision would double-count it on the host.
		if ( IsProxy ) return;

		var victimRoot = victimHorseRb.GameObject?.Root;
		PublicityCurrencyManager.NotifyRamHit( attackerRoot, victimRoot );
	}

	// --- Direction-Helpers ------------------------------------------------------

	private float ComputeSideSign( Vector3 pushDir )
	{
		Vector3 rightDir = WorldRotation.Right;
		rightDir.z = 0f;
		if ( rightDir.LengthSquared < 0.0001f ) rightDir = new Vector3( 0f, 1f, 0f );
		rightDir = rightDir.Normal;
		float sign = MathF.Sign( Vector3.Dot( pushDir, rightDir ) );
		return sign == 0f ? 1f : sign;
	}

	private Vector3 BuildFinalDirection( Vector3 pushDir )
	{
		Vector3 rightDir = WorldRotation.Right;
		rightDir.z = 0f;
		if ( rightDir.LengthSquared < 0.0001f ) rightDir = new Vector3( 0f, 1f, 0f );
		rightDir = rightDir.Normal;
		float sideSign = MathF.Sign( Vector3.Dot( pushDir, rightDir ) );
		if ( sideSign == 0f ) sideSign = 1f;
		Vector3 lateralDir = rightDir * sideSign;

		Vector3 horizontal = Vector3.Lerp( pushDir, lateralDir, LateralBias );
		if ( horizontal.LengthSquared < 0.0001f ) horizontal = pushDir;
		horizontal = horizontal.Normal;

		// Vertikalanteil reinmischen — Wagen hebt kurz ab für den Slop-Effekt.
		Vector3 mixed = horizontal * (1f - VerticalLift) + Vector3.Up * VerticalLift;
		return mixed.Normal;
	}

	// --- Identifikation des Opfers ----------------------------------------------

	private void FindOtherPlayerBodies( Collision collision, out Rigidbody horseRb, out Rigidbody chariotRb )
	{
		horseRb = null;
		chariotRb = null;

		var otherGo = collision.Other.GameObject;
		if ( otherGo is null ) return;

		// Fall 1: direkt in die Pferde des anderen Spielers gefahren.
		var otherPlayer = otherGo.Components.Get<PlayerCollisions>( FindMode.EverythingInSelfAndAncestors );
		if ( otherPlayer is not null && otherPlayer.Body != Body )
		{
			horseRb = otherPlayer.Body;
			chariotRb = FindChariotRigidbodyFor( horseRb );
			return;
		}

		// Fall 2: in den Wagen des anderen Spielers gefahren.
		var otherChariot = otherGo.Components.Get<ChariotPhysics>( FindMode.EverythingInSelfAndAncestors );
		if ( otherChariot is not null )
		{
			var otherHorseBody = otherChariot.HorsePairRb;
			if ( otherHorseBody is not null && otherHorseBody != Body )
			{
				horseRb = otherHorseBody;
				chariotRb = otherChariot.Body;
			}
		}
	}

	private Rigidbody FindChariotRigidbodyFor( Rigidbody horseRb )
	{
		if ( horseRb is null ) return null;
		var allChariots = Scene.GetAllComponents<ChariotPhysics>();
		foreach ( var cp in allChariots )
		{
			if ( cp is not null && cp.HorsePairRb == horseRb )
			{
				return cp.Body;
			}
		}
		return null;
	}
}
