using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>
/// Taranis (Blitze &amp; Wetter):
///   Normal — Blitz-Bombe: the caster's chariot is "charged". After a short fuse
///   (dodge window) it detonates: radius query around the Wagen node, all
///   PlayerDamageSystems inside take HP damage. Caster is exempt.
///
///   Ultimate — Götterfunke: the whole track is electrified. ALL players (caster
///   included) get a large speed boost. On every wall or player crash they take
///   bonus damage. The caster is immune for the duration. After UltimateDuration
///   everything reverts.
/// </summary>
public sealed class LightningPower : GodPower
{
	// ---------- Wagen Node (auto-resolved) ----------

	/// <summary>
	/// Name of the chariot's body node on the PlayerChariot prefab. Origin of the
	/// Blitz-Bombe and source of crash visuals. Resolved in <see cref="OnOwnerAssigned"/>.
	/// </summary>
	[Property, Group( "Wagen" )]
	public string WagenNodeName { get; set; } = "Wagen";

	private GameObject wagenNode;

	/// <summary>World position the Bomb was cast from on the last detonation. Hook your VFX/SFX here.</summary>
	public Vector3 LastLightningOrigin { get; private set; }

	// ---------- Normal: Blitz-Bombe ----------

	/// <summary>Dodge window — short timer between activation and detonation so opponents can swerve away.</summary>
	[Property, Group( "Blitz-Bombe (Normal)" )]
	public float FuseTime { get; set; } = 1.5f;

	/// <summary>Radius around the Wagen that gets zapped. World units.</summary>
	[Property, Group( "Blitz-Bombe (Normal)" )]
	public float BombRadius { get; set; } = 400f;

	/// <summary>Flat HP subtracted from every player caught in the radius.</summary>
	[Property, Group( "Blitz-Bombe (Normal)" )]
	public float BombDamage { get; set; } = 40f;

	[Property, Group( "Blitz-Bombe (Normal)" )]
	public string PlayerTag { get; set; } = "player";

	// ---------- Ultimate: Götterfunke ----------

	[Property, Group( "Götterfunke (Ultimate)" )]
	public float UltimateDuration { get; set; } = 10f;

	/// <summary>Speed multiplier applied to every player's ChariotPhysics for the duration. 1.0 = no change.</summary>
	[Property, Group( "Götterfunke (Ultimate)" )]
	public float SpeedBoost { get; set; } = 1.6f;

	/// <summary>Flat HP applied to a non-caster player every time they crash into a wall or another player during the surge.</summary>
	[Property, Group( "Götterfunke (Ultimate)" )]
	public float CrashDamage { get; set; } = 25f;

	/// <summary>Per-victim minimum gap between crash-damage applications, so a sticky collision doesn't drain HP per frame.</summary>
	[Property, Group( "Götterfunke (Ultimate)" )]
	public float CrashDamageCooldown { get; set; } = 0.4f;

	/// <summary>Collisions tagged with this never count as a "crash" (so driving over the floor does nothing).</summary>
	[Property, Group( "Götterfunke (Ultimate)" )]
	public string GroundTag { get; set; } = "ground";

	/// <summary>Wall tag — hitting any object carrying this tag triggers the crash damage.</summary>
	[Property, Group( "Götterfunke (Ultimate)" )]
	public string WallTag { get; set; } = "wall";

	private const string SpeedKey = "taranis_surge";

	private bool fuseLit = false;
	private bool ultimateActive = false;

	// ---------- Ultimate runtime state ----------

	private sealed class BoostedPlayer
	{
		public GameObject Root;
		public bool IsCaster;
		public ISpeedModifiable Speed;
		public ChariotPhysics Chariot;
		public PlayerDamageSystem Damage;
		public Action<Collision> Handler;
		public float LastCrashHitTime = float.NegativeInfinity;
	}

	private readonly List<BoostedPlayer> boosted = new();
	private PlayerDamageSystem casterDamage;
	private float casterOriginalDamageMultiplier;

	// ---------- Linger guard ----------

	/// <summary>
	/// PlayerItemTracker destroys this clone after LingerAfterNormal/Ultimate seconds.
	/// Both abilities use Invoke (fuse, surge timer) which is tied to this component's
	/// lifetime, so the linger MUST cover the effect duration or the callbacks never
	/// fire. We push the linger out automatically here instead of forcing every prefab
	/// to keep two numbers in sync by hand.
	/// </summary>
	protected override void OnAwake()
	{
		const float safetyPad = 0.5f;
		if ( LingerAfterNormal < FuseTime + safetyPad )
			LingerAfterNormal = FuseTime + safetyPad;
		if ( LingerAfterUltimate < UltimateDuration + safetyPad )
			LingerAfterUltimate = UltimateDuration + safetyPad;
	}

	// ---------- Owner Hookup ----------

	protected override void OnOwnerAssigned()
	{
		wagenNode = Owner.IsValid() ? FindDescendantByName( Owner, WagenNodeName ) : null;

		if ( wagenNode is null )
			Log.Warning( $"[LightningPower] Could not find '{WagenNodeName}' node under Owner '{Owner?.Name}'." );
	}

	private static GameObject FindDescendantByName( GameObject root, string name )
	{
		foreach ( var child in root.Children )
		{
			if ( child is null ) continue;
			if ( string.Equals( child.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase ) )
				return child;

			var deeper = FindDescendantByName( child, name );
			if ( deeper is not null ) return deeper;
		}
		return null;
	}

	// ---------- Normal: Blitz-Bombe ----------

	protected override bool CanActivate() => !fuseLit;

	protected override void OnActivate()
	{
		fuseLit = true;
		// Ladehinweis mit Countdown bis zur Detonation. Wird in Detonate() durch die
		// Trefferbilanz ersetzt.
		ResolveNotifier()?.ShowTimed( "Du lädst einen Blitz auf", FuseTime );
		// Charge jingle (Sound A → Sound B loop), proximity on the user. Lives on the
		// persistent player module so it keeps playing after this clone is destroyed.
		ResolveNormalSfx()?.StartTaranisCharge();
		// Looping charge VFX on the Wagen, broadcast to everyone. Destroyed by TaranisStrike().
		ResolveNormalVfx()?.StartTaranisCharge();
		// Detonation will use the LIVE position of the Wagen, not a snapshot — the
		// chariot keeps moving during the fuse, the bomb travels with it.
		Invoke( FuseTime, Detonate );
	}

	private void Detonate()
	{
		Vector3 origin = wagenNode.IsValid()
			? wagenNode.WorldPosition
			: (Owner.IsValid() ? Owner.WorldPosition : WorldPosition);

		LastLightningOrigin = origin;
		// Fully charged → Sound C (stops the charge loop), proximity on the user.
		ResolveNormalSfx()?.TaranisCharged();
		// Charge VFX off, strike VFX on the Wagen, broadcast to everyone.
		ResolveNormalVfx()?.TaranisStrike();

		// Tags are inherited from ancestors, so FindAllWithTag returns the chariot root
		// AND every node under it (Wagen, Antrieb, Camera, ...). Identity therefore has
		// to be decided on the resolved PlayerDamageSystem — one per player — and never
		// on the tagged GameObject itself.
		var casterDamageSystem = Owner.IsValid()
			? Owner.Components.Get<PlayerDamageSystem>( FindMode.EverythingInSelfAndDescendants )
			: null;

		int hitCount = 0;
		float radiusSq = BombRadius * BombRadius;
		var alreadyHit = new HashSet<PlayerDamageSystem>();

		foreach ( var tagged in Scene.FindAllWithTag( PlayerTag ) )
		{
			if ( !tagged.IsValid() ) continue;

			var dmg = tagged.Components.Get<PlayerDamageSystem>( FindMode.EverythingInSelfAndDescendants );
			if ( dmg is null ) continue;

			if ( dmg == casterDamageSystem ) continue; // caster is exempt
			if ( !alreadyHit.Add( dmg ) ) continue;    // one hit per player, not per tagged node

			// Measure from the damage system's own node (the Wagen body) rather than the
			// tagged node — the hierarchy spans hundreds of units, so distance has to come
			// from a fixed point per player or the radius means nothing.
			if ( Vector3.DistanceBetweenSquared( dmg.WorldPosition, origin ) > radiusSq )
				continue;

			dmg.Damage( BombDamage );
			hitCount++;
		}

		// Trefferbilanz für den Caster (der Caster ist oben via casterDamageSystem
		// ausgenommen, zählt sich also nicht selbst).
		ResolveNotifier()?.Show(
			hitCount == 0 ? "Du hast niemanden getroffen"
			: hitCount == 1 ? "Du hast 1 Spieler Schaden gemacht"
			: $"Du hast {hitCount} Spielern Schaden gemacht" );

		Log.Info( $"[LightningPower] Blitz-Bombe detonated at {origin} → {hitCount} player(s) damaged for {BombDamage} HP." );
	}

	// ---------- Ultimate: Götterfunke ----------

	protected override bool CanActivateUltimate() => !ultimateActive;

	protected override void OnActivateUltimate()
	{
		ultimateActive = true;
		boosted.Clear();
		ResolveNotifier()?.ShowTimed( "Alle aufgeladen – Crashs tun weh", UltimateDuration );
		// Sound A + voice, worldwide.
		GodPowersUltimateSfxmodule.Instance?.PlayTaranisUltimate();
		// Sky image, per-player (each aimed at their own chariot).
		GodPowersImageModule.Instance?.ShowTaranisImage();

		// Caster immunity: stash and zero their incoming-damage multiplier.
		casterDamage = Owner.IsValid()
			? Owner.Components.Get<PlayerDamageSystem>( FindMode.EverythingInSelfAndDescendants )
			: null;

		if ( casterDamage is not null )
		{
			casterOriginalDamageMultiplier = casterDamage.DamageMultiplier;
			casterDamage.DamageMultiplier = 0f;
		}

		// Same tag-inheritance caveat as Detonate: one tagged node per player is a lie,
		// so dedupe on the resolved PlayerDamageSystem. Without this the caster's own
		// child nodes come back as separate, non-caster entries and each one subscribes
		// its own crash handler — the caster gets zapped by their own Ultimate and
		// everyone else takes CrashDamage once per node.
		var seen = new HashSet<PlayerDamageSystem>();

		foreach ( var tagged in Scene.FindAllWithTag( PlayerTag ) )
		{
			if ( !tagged.IsValid() ) continue;

			var damage = tagged.Components.Get<PlayerDamageSystem>( FindMode.EverythingInSelfAndDescendants );
			if ( damage is null ) continue;
			if ( !seen.Add( damage ) ) continue;

			// Resolve the rest from the player root, not from `tagged` — which node of the
			// chariot surfaced first is arbitrary, and a deep one wouldn't see components
			// sitting on its siblings.
			var playerRoot = damage.GameObject.Root;

			var entry = new BoostedPlayer
			{
				Root = playerRoot,
				// Component identity, not GameObject identity — the only reliable way to
				// tell "this is the caster" when every child reports the player tag.
				IsCaster = (casterDamage is not null && damage == casterDamage),
				Speed = playerRoot.Components.Get<ISpeedModifiable>( FindMode.EverythingInSelfAndDescendants ),
				Chariot = playerRoot.Components.Get<ChariotPhysics>( FindMode.EverythingInSelfAndDescendants ),
				Damage = damage,
			};

			entry.Speed?.SetSpeedMultiplier( SpeedKey, SpeedBoost );

			// Subscribe to crash impacts. The caster also subscribes for symmetry/debug,
			// but the handler bails out on entry.IsCaster so they never take damage.
			if ( entry.Chariot.IsValid() )
			{
				var local = entry; // capture
				entry.Handler = collision => OnPlayerCrash( local, collision );
				entry.Chariot.ImpactStarted += entry.Handler;
			}

			boosted.Add( entry );
		}

		Log.Info( $"[LightningPower] Götterfunke active for {UltimateDuration}s ({boosted.Count} players boosted, caster immune)." );

		Invoke( UltimateDuration, EndUltimate );
	}

	private void OnPlayerCrash( BoostedPlayer entry, Collision collision )
	{
		if ( !ultimateActive ) return;
		if ( entry.IsCaster ) return; // immune
		if ( entry.Damage is null ) return;

		if ( Time.Now - entry.LastCrashHitTime < CrashDamageCooldown ) return;

		var otherGo = collision.Other.GameObject;
		if ( !otherGo.IsValid() ) return;

		if ( HasTagInChain( otherGo, GroundTag ) ) return;

		bool isWall = HasTagInChain( otherGo, WallTag );
		bool isPlayer = HasTagInChain( otherGo, PlayerTag );
		if ( !isWall && !isPlayer ) return;

		entry.LastCrashHitTime = Time.Now;
		entry.Damage.Damage( CrashDamage );
	}

	private static bool HasTagInChain( GameObject from, string tag )
	{
		if ( string.IsNullOrEmpty( tag ) ) return false;
		var node = from;
		while ( node.IsValid() )
		{
			if ( node.Tags.Has( tag ) ) return true;
			node = node.Parent;
		}
		return false;
	}

	private void EndUltimate()
	{
		if ( !ultimateActive ) return;
		ultimateActive = false;

		foreach ( var entry in boosted )
		{
			entry.Speed?.ClearSpeedMultiplier( SpeedKey );
			if ( entry.Chariot.IsValid() && entry.Handler is not null )
				entry.Chariot.ImpactStarted -= entry.Handler;
		}
		boosted.Clear();

		if ( casterDamage is not null )
		{
			casterDamage.DamageMultiplier = casterOriginalDamageMultiplier;
			casterDamage = null;
		}
	}

	// ---------- Safety ----------

	protected override void OnDisabled()
	{
		// Power can be destroyed mid-effect (item swap, scene unload). Make sure the
		// surge is fully unwound — otherwise players stay boosted and the caster
		// stays immune forever.
		if ( ultimateActive )
			EndUltimate();
	}
}
