using Sandbox;
using System;

/// <summary>
/// Dionysos:
///   Normal — Trauben-Schütze: schießt mehrere Trauben in einem Spread-Kegel
///   nach vorne (kein Auto-Aim, Bounce-Verhalten kommt vom Projektil-Prefab).
///
///   Ultimate — Trunkenheit am Steuer: addiert DrunkDuration Sekunden auf den
///   TestControlls-Drunk-Timer aller anderen Spieler. Das Power-Objekt kann sofort
///   entsorgt werden (kein Linger nötig); mehrfache Ults stapeln sauber, weil
///   der Timer auf TestControlls liegt und sich nur addiert.
///   Der bunte Filter wird separat angebunden via TestControlls.OnDrunkChanged.
/// </summary>
public sealed class DionysosPower : GodPower
{
	// ---------- Grape Shooter (Normal) ----------

	[Property, Group( "Grape Shooter" )]
	public GameObject GrapeProjectilePrefab { get; set; }

	/// <summary>Transform dessen Rotation als Schussbasis dient (Pferd des Test-Prefabs). Fällt sonst auf Owner zurück.</summary>
	[Property, Group( "Grape Shooter" )]
	public GameObject HorseReference { get; set; }

	[Property, Group( "Grape Shooter" )]
	public int GrapeCount { get; set; } = 5;

	/// <summary>Spread ±SpreadAngleDegrees um die Vorwärtsrichtung des Pferdes.</summary>
	[Property, Group( "Grape Shooter" ), Range( 0f, 180f )]
	public float SpreadAngleDegrees { get; set; } = 45f;

	[Property, Group( "Grape Shooter" )]
	public float ProjectileSpeed { get; set; } = 3000f;

	[Property, Group( "Grape Shooter" )]
	public float SpawnForwardOffset { get; set; } = 50f;

	[Property, Group( "Grape Shooter" )]
	public float SpawnHeightOffset { get; set; } = 30f;

	/// <summary>Auto-resolved "Antrieb" node from the owning chariot. Preferred over HorseReference when valid.</summary>
	private GameObject antriebNode;

	private const string AntriebName = "Antrieb";
	private const float AntriebForwardOffset = 2f;

	// ---------- Drunk Drive (Ultimate) ----------

	[Property, Group( "Drunk Drive" )]
	public float DrunkDuration { get; set; } = 8f;

	[Property, Group( "Drunk Drive" )]
	public string PlayerTag { get; set; } = "player";

	// ---------- Owner Hookup ----------

	protected override void OnOwnerAssigned()
	{
		antriebNode = Owner.IsValid() ? FindDescendantByName( Owner, AntriebName ) : null;

		if ( antriebNode is null )
			Log.Warning( $"[DionysosPower] Konnte '{AntriebName}'-Node im Owner '{Owner?.Name}' nicht finden." );
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

	// ---------- Normal ----------

	protected override void OnActivate()
	{
		//Log.Info( "Dionyoss ist dabei meine Freunde" );
		ResolveNotifier()?.Show( "Schuss" );
		// Random grape clip (A or B), proximity on the user.
		ResolveNormalSfx()?.PlayDionysosGrapes();

		if ( !GrapeProjectilePrefab.IsValid() )
		{
			Log.Warning( "[DionysosPower] GrapeProjectilePrefab nicht gesetzt." );
			return;
		}

		Vector3 spawnPos;
		Rotation baseRot;
		Vector3 fireDir;

		if ( antriebNode.IsValid() )
		{
			// Direction comes from the Owner/chariot root — the Antrieb node's own local
			// rotation can be flipped relative to the chariot (which would shoot bullets
			// backwards), so we only borrow its position.
			var dirBasis = Owner.IsValid() ? Owner : antriebNode;
			baseRot = dirBasis.WorldRotation;
			// Chariot's authored "forward" axis points backwards visually — flip it.
			fireDir = -baseRot.Forward;
			spawnPos = antriebNode.WorldPosition + fireDir * AntriebForwardOffset;
		}
		else
		{
			var basis = HorseReference.IsValid() ? HorseReference : Owner;
			if ( !basis.IsValid() )
			{
				Log.Warning( "[DionysosPower] Keine Schussbasis (Antrieb, HorseReference und Owner ungültig)." );
				return;
			}

			baseRot = basis.WorldRotation;
			fireDir = -baseRot.Forward;
			spawnPos = basis.WorldPosition
				+ fireDir * SpawnForwardOffset
				+ Vector3.Up * SpawnHeightOffset;
		}

		// Build a rotation whose forward = fireDir, so the spread cone stays correct.
		Rotation shotBase = Rotation.LookAt( fireDir, Vector3.Up );

		for ( int i = 0; i < GrapeCount; i++ )
		{
			float yawOffset = Random.Shared.Float( -SpreadAngleDegrees, SpreadAngleDegrees );
			Rotation shotRot = shotBase * Rotation.FromYaw( yawOffset );

			var clone = GrapeProjectilePrefab.Clone( spawnPos, shotRot );
			if ( !clone.IsValid() ) continue;

			var rb = clone.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
			if ( rb is not null )
				rb.Velocity = shotRot.Forward * ProjectileSpeed;
		}
	}

	// ---------- Ultimate ----------

	protected override void OnActivateUltimate()
	{
		// Sound A + voice, worldwide.
		GodPowersUltimateSfxmodule.Instance?.PlayDionysosUltimate();
		// Sky image, per-player (each aimed at their own chariot).
		GodPowersImageModule.Instance?.ShowDionysosImage();

		Guid casterId = Guid.Empty;
		if ( Owner.IsValid() )
		{
			var ownerControls = Owner.Components.Get<TestControlls>( FindMode.EverythingInSelfAndDescendants );
			if ( ownerControls is not null )
				casterId = ownerControls.PlayerId;
		}

		int hits = 0;
		var players = Scene.FindAllWithTag( PlayerTag );
		foreach ( var player in players )
		{
			var controls = player.Components.Get<TestControlls>( FindMode.EverythingInSelfAndDescendants );
			if ( controls is null ) continue;

			// Filter out the caster via PlayerId so we don't drunk-drive ourselves.
			if ( casterId != Guid.Empty && controls.PlayerId == casterId ) continue;

			// [Rpc.Owner] routes to the player's owning peer (they're a proxy here on the
			// caster); stacks with any timer already running.
			controls.AddDrunkTimeRpc( DrunkDuration );

			hits++;
		}

		// Caster-lokale Bilanz (der Caster selbst ist oben schon herausgefiltert).
		ResolveNotifier()?.Show( hits == 1
			? "Du hast 1 Spieler betrunken gemacht"
			: $"Du hast {hits} Spieler betrunken gemacht" );

		Log.Info( $"[DionysosPower] {hits} Spieler bekommen +{DrunkDuration}s Drunk" );
	}
}
