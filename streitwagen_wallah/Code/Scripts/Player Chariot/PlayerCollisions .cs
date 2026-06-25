using Sandbox;
using System;

/// <summary>
/// Friendslop-Style Spieler-gegen-Spieler Kollisionen. Das "Gehirn" des Rammens; sitzt
/// auf dem Wagen (gleiches GameObject wie ChariotPhysics und das Wagen-Rigidbody).
///
/// Bekommt Treffer NICHT über den eigenen ICollisionListener (der ist absichtlich aus),
/// sondern über zwei Weiterleitungen, jeweils auf dem Body, der wirklich kollidiert:
/// ChariotPhysics (Wagen) und TestControlls (Antrieb / Pferdepaar) rufen beide
/// <see cref="HandleRamCollision"/>. So zählt ein Treffer der Pferde ODER des Wagens
/// gleich als Friendslop-Treffer.
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
		return; 
		//TryRam( other );
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision other ) { }
	void Component.ICollisionListener.OnCollisionStop( CollisionStop other ) { }

	/// <summary>
	/// Entry point for a ram, called by the two forwarders that sit on the physics
	/// bodies: ChariotPhysics (Wagen) for chariot contacts and TestControlls
	/// (Antrieb / horse pair) for horse contacts. Both funnel here so a hit from either
	/// body is scored the same way.
	/// </summary>
	public void HandleRamCollision( Collision collision )
	{
		TryRam( collision );
	}

	// --- Ram-Kern ---------------------------------------------------------------

	private void TryRam( Collision collision )
	{
		if ( Body is null ) return;
		if ( Time.Now - _lastRamTime < Cooldown ) return;

		FindOtherPlayerBodies( collision, out ChariotPhysics otherChariot, out Rigidbody otherHorseRb, out Rigidbody otherChariotRb );
		if ( otherChariot is null || otherHorseRb is null || otherHorseRb == Body ) return;

		// Push direction: shove the victim away from US, horizontally. Derived from the two
		// bodies' positions instead of collision.Contact.Normal — across the network the
		// contact normal can flip or jitter (the victim is a proxy and the contact is
		// regenerated locally), which made hits fire in random directions and feel "weird".
		// Body-to-body is stable.
		Vector3 pushDirRaw = (otherHorseRb.WorldPosition - Body.WorldPosition).WithZ( 0f );
		if ( pushDirRaw.LengthSquared < 0.0001f )
			pushDirRaw = (-collision.Contact.Normal).WithZ( 0f ); // stacked centres → fall back to the contact normal
		if ( pushDirRaw.LengthSquared < 0.0001f ) return;
		Vector3 pushDir = pushDirRaw.Normal;

		// Closing speed from OUR OWN velocity toward them — deterministic. The old code
		// subtracted the victim's velocity, but for a networked victim that's a proxy value
		// that's frequently stale or zero, so the closing speed jittered and the hit randomly
		// failed the gate below (the "non-existent impact"). We own our body, so this can't
		// desync; in a head-on each chariot fires with its own approach speed, so both launch.
		float approach = Vector3.Dot( Body.Velocity.WithZ( 0f ), pushDir );
		if ( approach < MinClosingSpeed ) return;

		Vector3 finalDir = BuildFinalDirection( pushDir );

		// SharpSteer-Multiplikator. Wenn dein HorseController eine bool-Property
		// IsSharpSteering hat, hier einkommentieren:
		float sharpMult = 1f;
		// var hc = Components.Get<HorseController>();
		// if ( hc is not null && hc.IsSharpSteering ) sharpMult = SharpSteerMultiplier;

		float weightMult = AttackerStats is not null ? AttackerStats.WeightMultiplier : 1f;

		float magnitude = (BaseImpulse + ImpulsePerClosingSpeed * approach) * sharpMult * weightMult;
		magnitude = MathF.Min( magnitude, MaxImpulse );

		float sideSign = ComputeSideSign( pushDir );

		// Build the impulses here (attacker side — we have the contact + closing speed)…
		Vector3 horseImpulse = finalDir * magnitude;
		Vector3 horseAngular = Vector3.Up * (magnitude * AngularImpulseFactor * sideSign);

		Vector3 chariotImpulse = Vector3.Zero;
		Vector3 chariotAngular = Vector3.Zero;
		if ( otherChariotRb is not null && ChariotImpulseRatio > 0f )
		{
			// Opfer-Wagen — fliegt stärker und dreht sich wilder.
			float chariotMag = magnitude * ChariotImpulseRatio;
			chariotImpulse = finalDir * chariotMag;
			chariotAngular = Vector3.Up * (chariotMag * AngularImpulseFactor * ChariotExtraSpin * sideSign);
		}

		// …but APPLY them on the victim's owner. Doing it directly here would just nudge a
		// network proxy and get snapped back next snapshot (the "static hit" against real
		// players). The Rpc routes to whoever simulates the victim's bodies.
		otherChariot.ApplyRamKnockback( horseImpulse, horseAngular, chariotImpulse, chariotAngular );

		_lastRamTime = Time.Now;

		// Optional debris burst — only on a confirmed ram, and only if a prefab is
		// actually assigned. (The field is left empty in the prefab, so the old
		// unconditional Clone() up top threw an NRE the moment ramming fired.)
		if ( CollisionDebrisPrefab is not null )
			CollisionDebrisPrefab.Clone( WorldPosition );

		AwardRamCurrency( otherHorseRb );

		if ( DebugLog )
		{
			Log.Info( $"[PlayerCollisions {GameObject.Name}] approach={approach:F1} | sharp={(sharpMult > 1f)} | " +
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

	/// <summary>
	/// Resolves the victim's two rigidbodies (horse pair + chariot) from whatever part
	/// of them we actually touched. The victim's <see cref="ChariotPhysics"/> is the
	/// single source of truth for both bodies, so we find it from the *other* object's
	/// root and read them straight off it.
	///
	/// Searched from the root (not self-and-ancestors) on purpose: on every player both
	/// ChariotPhysics and PlayerCollisions live on the Wagen, which is a *sibling* of the
	/// horses — not an ancestor. Our ram boxes sit on the horses and usually contact the
	/// victim's horses first, so an ancestor-only search would miss the victim on exactly
	/// the hits that matter. The root check also rejects ground hits and our own bodies
	/// (the hitch joint has EnableCollision, so horse and cart can touch each other).
	/// </summary>
	private void FindOtherPlayerBodies( Collision collision, out ChariotPhysics otherChariot, out Rigidbody horseRb, out Rigidbody chariotRb )
	{
		otherChariot = null;
		horseRb = null;
		chariotRb = null;

		var otherRoot = collision.Other.GameObject?.Root;
		if ( otherRoot is null || otherRoot == GameObject.Root ) return;

		otherChariot = otherRoot.Components.Get<ChariotPhysics>( FindMode.EverythingInSelfAndDescendants );
		if ( otherChariot is null ) return;

		horseRb = otherChariot.HorsePairRb;
		chariotRb = otherChariot.Body;
	}
}
