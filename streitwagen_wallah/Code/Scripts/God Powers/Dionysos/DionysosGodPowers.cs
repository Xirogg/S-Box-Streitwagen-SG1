using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>
/// Dionysos:
///   Normal — Trauben-Schütze: schießt mehrere Trauben in einem Spread-Kegel
///   nach vorne (kein Auto-Aim, Bounce-Verhalten kommt vom Projektil-Prefab).
///
///   Ultimate — Trunkenheit am Steuer: alle Spieler außer dem Caster
///   bekommen invertierte Steuerung für DrunkDuration Sekunden.
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

	// ---------- Drunk Drive (Ultimate) ----------

	[Property, Group( "Drunk Drive" )]
	public float DrunkDuration { get; set; } = 8f;

	[Property, Group( "Drunk Drive" )]
	public string PlayerTag { get; set; } = "player";

	private readonly List<TestControlls> drunkPlayers = new();
	private bool drunkActive;

	// ---------- Normal ----------

	protected override void OnActivate()
	{
		//Log.Info( "Dionyoss ist dabei meine Freunde" );
		if ( !GrapeProjectilePrefab.IsValid() )
		{
			Log.Warning( "[DionysosPower] GrapeProjectilePrefab nicht gesetzt." );
			return;
		}

		var basis = HorseReference.IsValid() ? HorseReference : Owner;
		if ( !basis.IsValid() )
		{
			Log.Warning( "[DionysosPower] Keine Schussbasis (HorseReference und Owner ungültig)." );
			return;
		}

		Rotation baseRot = basis.WorldRotation;
		Vector3 spawnPos = basis.WorldPosition
			+ baseRot.Forward * SpawnForwardOffset
			+ Vector3.Up * SpawnHeightOffset;

		for ( int i = 0; i < GrapeCount; i++ )
		{
			float yawOffset = Random.Shared.Float( -SpreadAngleDegrees, SpreadAngleDegrees );
			Rotation shotRot = baseRot * Rotation.FromYaw( yawOffset );

			var clone = GrapeProjectilePrefab.Clone( spawnPos, shotRot );
			if ( !clone.IsValid() ) continue;

			var rb = clone.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
			if ( rb is not null )
				rb.Velocity = shotRot.Forward * ProjectileSpeed;
		}
	}

	// ---------- Ultimate ----------

	protected override bool CanActivateUltimate() => !drunkActive;

	protected override void OnActivateUltimate()
	{
		//Log.Info( "Drunk active" );
		drunkActive = true;
		drunkPlayers.Clear();

		Guid casterId = Guid.Empty;
		if ( Owner.IsValid() )
		{
			var ownerControls = Owner.Components.Get<TestControlls>( FindMode.EverythingInSelfAndDescendants );
			if ( ownerControls is not null )
				casterId = ownerControls.PlayerId;
		}

		var players = Scene.FindAllWithTag( PlayerTag );
		foreach ( var player in players )
		{
			var controls = player.Components.Get<TestControlls>( FindMode.EverythingInSelfAndDescendants );
			if ( controls is null ) continue;
			if ( casterId != Guid.Empty && controls.PlayerId == casterId ) continue;

			controls.SetDrunk( true );
			drunkPlayers.Add( controls );
		}

		//Log.Info( $"[DionysosPower] {drunkPlayers.Count} Spieler betrunken für {DrunkDuration}s" );

		Invoke( DrunkDuration, RevertDrunk );
	}

	private void RevertDrunk()
	{
		foreach ( var controls in drunkPlayers )
		{
			if ( controls.IsValid() )
				controls.SetDrunk( false );
		}
		drunkPlayers.Clear();
		drunkActive = false;
	}

	protected override void OnDisabled()
	{
		if ( drunkActive )
			RevertDrunk();
	}
}
