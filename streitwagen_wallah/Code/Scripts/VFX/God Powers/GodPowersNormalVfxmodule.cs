using Sandbox;
using System;

namespace Sandbox;

/// <summary>
/// Per-player VFX module for the NORMAL god-power abilities. Put ONE on every player
/// prefab — the same object that carries <see cref="GodPowersNormalSfxmodule"/>. Each
/// player's own short-lived GodPower clone (which runs on that player's client) calls
/// the matching method here on activation; because the effects live on the persistent
/// player, they keep playing after the GodPower clone is destroyed.
///
/// All spawns are BROADCAST so every client sees the effect on the casting player's
/// Wagen node. Each client then spawns its OWN local (non-networked) copy — nothing is
/// NetworkSpawn'd, so joining clients never get duplicates and the lifetimes are managed
/// per client. This mirrors how the SFX module broadcasts each clip.
///
/// Lifetimes: the three VFX prefabs are all looping emitters that never self-destruct, so
/// this module owns their teardown:
///   - Lightning_Charge — spawned on charge, kept alive until the strike (or a safety cap).
///   - Lightning_Strike — spawned on detonation, auto-destroyed after StrikeDuration.
///   - Rauch_Laverna    — spawned on the steal, auto-destroyed after SmokeDuration.
/// </summary>
public sealed class GodPowersNormalVfxmodule : Component
{
	/// <summary>Stable ids for the effects, sent over the wire so no prefab ref travels.</summary>
	private enum VfxClip
	{
		LightningCharge,   // Taranis — charge begins (loops until the strike)
		LightningStrike,   // Taranis — fully charged / detonation
		LavernaSmoke,      // Laverna — item steal
	}

	// ───────── Taranis — Blitz-Bombe ─────────

	/// <summary>Looping charge effect, spawned on the Wagen while the bomb fuses.</summary>
	[Property, Group( "Taranis" )] public GameObject LightningChargePrefab { get; set; }

	/// <summary>Strike effect, spawned on the Wagen when the bomb detonates.</summary>
	[Property, Group( "Taranis" )] public GameObject LightningStrikePrefab { get; set; }

	/// <summary>Seconds the strike effect stays alive before it is destroyed.</summary>
	[Property, Group( "Taranis" ), Range( 0f, 10f )]
	public float StrikeDuration { get; set; } = 1.5f;

	/// <summary>
	/// Safety cap (s). If the strike RPC never arrives on a client, the looping charge
	/// effect would play forever — this destroys it after the cap no matter what. Keep it
	/// comfortably above the power's FuseTime.
	/// </summary>
	[Property, Group( "Taranis" ), Range( 0f, 20f )]
	public float MaxChargeSeconds { get; set; } = 6f;

	// ───────── Laverna — Item-Dieb ─────────

	/// <summary>Smoke effect, spawned on the caster's Wagen when they cast the steal.</summary>
	[Property, Group( "Laverna" )] public GameObject RauchLavernaPrefab { get; set; }

	/// <summary>Seconds the smoke effect stays alive before it is destroyed.</summary>
	[Property, Group( "Laverna" ), Range( 0f, 20f )]
	public float SmokeDuration { get; set; } = 4f;

	// ───────── Origin ─────────

	/// <summary>
	/// Node the effects are parented to (so they follow the moving chariot). Leave empty
	/// to auto-resolve the "Wagen" body node by name on start.
	/// </summary>
	[Property, Group( "Origin" )] public GameObject VfxOrigin { get; set; }

	/// <summary>Name of the chariot body node used when <see cref="VfxOrigin"/> is empty.</summary>
	[Property, Group( "Origin" )] public string WagenNodeName { get; set; } = "Wagen";

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = false;

	/// <summary>This client's live charge instance, destroyed when the strike lands.</summary>
	private GameObject chargeInstance;

	protected override void OnStart()
	{
		if ( !VfxOrigin.IsValid() )
			VfxOrigin = FindDescendantByName( GameObject.Root, WagenNodeName ) ?? GameObject;
	}

	// ════════════════════════════ Public API ════════════════════════════
	// Called owner-locally by the GodPower clone; each call broadcasts to all clients.

	/// <summary>Taranis normal: start the looping charge effect on the Wagen.</summary>
	public void StartTaranisCharge() => Broadcast( VfxClip.LightningCharge );

	/// <summary>Taranis normal: detonation — stop the charge and play the strike.</summary>
	public void TaranisStrike() => Broadcast( VfxClip.LightningStrike );

	/// <summary>Laverna normal: puff of smoke on the caster's Wagen.</summary>
	public void PlayLavernaSteal() => Broadcast( VfxClip.LavernaSmoke );

	// ════════════════════════════ Networking ════════════════════════════

	private void Broadcast( VfxClip clip )
	{
		if ( Networking.IsActive )
			PlayVfxRpc( (int)clip );
		else
			PlayLocal( clip ); // editor / single-player
	}

	[Rpc.Broadcast]
	private void PlayVfxRpc( int clip ) => PlayLocal( (VfxClip)clip );

	// ════════════════════════════ Spawning ════════════════════════════

	private void PlayLocal( VfxClip clip )
	{
		switch ( clip )
		{
			case VfxClip.LightningCharge: SpawnCharge(); break;
			case VfxClip.LightningStrike: SpawnStrike(); break;
			case VfxClip.LavernaSmoke: SpawnSmoke(); break;
		}
	}

	private void SpawnCharge()
	{
		// Defensive: a leftover charge (e.g. a missed strike) shouldn't stack.
		DestroyChargeInstance();

		var inst = Spawn( LightningChargePrefab );
		if ( !inst.IsValid() ) return;

		chargeInstance = inst;

		// Fail-safe teardown: the charge loops forever, so guarantee it dies even if the
		// strike broadcast never reaches this client. Captured so a later charge isn't
		// killed by an earlier timer.
		var captured = inst;
		Invoke( MaxChargeSeconds, () =>
		{
			if ( captured.IsValid() ) captured.Destroy();
		} );

		if ( DebugLog ) Log.Info( "[NormalVfx] Taranis charge spawned." );
	}

	private void SpawnStrike()
	{
		DestroyChargeInstance(); // charge is over — the bolt takes its place

		var inst = Spawn( LightningStrikePrefab );
		if ( !inst.IsValid() ) return;

		var captured = inst;
		Invoke( StrikeDuration, () =>
		{
			if ( captured.IsValid() ) captured.Destroy();
		} );

		if ( DebugLog ) Log.Info( "[NormalVfx] Taranis strike spawned." );
	}

	private void SpawnSmoke()
	{
		var inst = Spawn( RauchLavernaPrefab );
		if ( !inst.IsValid() ) return;

		var captured = inst;
		Invoke( SmokeDuration, () =>
		{
			if ( captured.IsValid() ) captured.Destroy();
		} );

		if ( DebugLog ) Log.Info( "[NormalVfx] Laverna smoke spawned." );
	}

	/// <summary>
	/// Clone <paramref name="prefab"/> as a purely local effect parented to the Wagen so
	/// it rides along with the chariot. Explicitly non-networked — every client spawns its
	/// own copy via the broadcast, so networking it would double up and break late joiners.
	/// </summary>
	private GameObject Spawn( GameObject prefab )
	{
		if ( !prefab.IsValid() )
		{
			if ( DebugLog ) Log.Warning( "[NormalVfx] Missing prefab — nothing spawned." );
			return null;
		}

		var origin = VfxOrigin.IsValid()
			? VfxOrigin
			: (FindDescendantByName( GameObject.Root, WagenNodeName ) ?? GameObject);

		var inst = prefab.Clone();
		if ( !inst.IsValid() ) return null;

		inst.NetworkMode = NetworkMode.Never; // strictly local visual
		inst.SetParent( origin, false );      // keep local transform → sits at the Wagen origin
		inst.LocalPosition = Vector3.Zero;
		inst.LocalRotation = Rotation.Identity;
		return inst;
	}

	private void DestroyChargeInstance()
	{
		if ( chargeInstance.IsValid() ) chargeInstance.Destroy();
		chargeInstance = null;
	}

	protected override void OnDisabled()
	{
		// Player teardown / scene unload — don't leave a looping charge hanging.
		DestroyChargeInstance();
	}

	// ════════════════════════════ Helpers ════════════════════════════

	private static GameObject FindDescendantByName( GameObject root, string name )
	{
		if ( !root.IsValid() || string.IsNullOrEmpty( name ) ) return null;

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
}
