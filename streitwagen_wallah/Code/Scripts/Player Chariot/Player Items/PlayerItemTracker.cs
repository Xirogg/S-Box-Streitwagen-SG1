using Sandbox;
using System;

namespace Sandbox;

/// <summary>
/// One-slot item holder for a player. The player prefab no longer carries any
/// GodPower components — they're cloned in here on pickup, used once, and then
/// destroyed after their linger duration.
///
/// Lifecycle:
///   1. ItemPrefab on the host picks a random (key, prefab) and calls GrantItemRpc.
///   2. GrantItemRpc runs on the OWNING client. It clones the prefab as a child of
///      ItemHost, sets the GodPower's Owner to PlayerRoot, and writes HeldItemKey
///      ([Sync], so other clients' HUDs update too).
///   3. Owner presses UseAction (modifier = Ultimate). TryActivate runs the effect,
///      flips IsSpent.
///   4. Tracker schedules destruction after the power's ActiveLingerDuration so any
///      timed after-effect (drunk, judgement) finishes cleanly.
///   5. Destroy → HeldItemKey cleared → HUD shows "none" → player can pick up again.
///
/// Networking:
///   - Lives on the player chariot (owner-owned). [Sync] HeldItemKey is owner-auth.
///   - The cloned power instance is OWNER-LOCAL only (other clients don't have it,
///     they just see HeldItemKey for HUD). Effects that need to propagate use the
///     existing networked systems (PlayerDamageSystem, TestControlls.SetDrunk, etc).
/// </summary>
public sealed class PlayerItemTracker : Component
{
	/// <summary>
	/// Where the spawned GodPower is parented. Defaults to this GameObject if unset.
	/// Point this at a dedicated child (e.g. "PowerScripts") if you want spawned
	/// powers to live somewhere specific in the hierarchy.
	/// </summary>
	[Property, Group( "Items" )]
	public GameObject ItemHost { get; set; }

	/// <summary>
	/// The player root passed to the spawned GodPower as its Owner. Subclasses
	/// resolve player references (horse, damage system, ...) from this. Defaults to
	/// the topmost ancestor of this component.
	/// </summary>
	[Property, Group( "Items" )]
	public GameObject PlayerRoot { get; set; }

	[Property, Group( "Input" )]
	public string UseAction { get; set; } = "UseItem";

	/// <summary>Hold while pressing UseAction to fire the Ultimate. Leave empty to disable.</summary>
	[Property, Group( "Input" )]
	public string UltimateModifierAction { get; set; } = "Run";

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = true;

	/// <summary>Empty = no item held. UI reads this; replicated owner → others.</summary>
	[Sync] public string HeldItemKey { get; set; } = "";

	// Owner-side runtime — the cloned instance only exists on the owning client.
	private GameObject heldInstance;
	private GodPower heldPower;

	/// <summary>
	/// True while an item is held — including the linger phase after activation,
	/// so the box rejects a new pickup until the previous use fully cleans up.
	/// </summary>
	public bool HasItem => !string.IsNullOrEmpty( HeldItemKey );

	public GodPower HeldPower => heldPower;

	public event Action<string, GodPower> OnItemGranted;
	public event Action<string, GodPower> OnItemUsed;

	protected override void OnStart()
	{
		if ( !ItemHost.IsValid() ) ItemHost = GameObject;
		if ( !PlayerRoot.IsValid() ) PlayerRoot = FindRoot( GameObject );

		// Explicitly clear the slot on the owning client. The default value of the
		// [Sync] property is already "", but this guards against any stale state
		// surviving a re-spawn or a hot-reload, so the player never starts the
		// game with an item.
		if ( !Network.IsProxy )
		{
			if ( !string.IsNullOrEmpty( HeldItemKey ) )
			{
				if ( DebugLog )
					Log.Warning( $"[PlayerItemTracker] OnStart found stale HeldItemKey='{HeldItemKey}' — clearing." );
				HeldItemKey = "";
			}

			if ( heldInstance.IsValid() )
			{
				heldInstance.Destroy();
				heldInstance = null;
				heldPower = null;
			}
		}
	}

	protected override void OnDestroy()
	{
		if ( heldInstance.IsValid() )
			heldInstance.Destroy();
	}

	/// <summary>
	/// Called by the host's ItemPrefab on pickup. Runs on the OWNING client so the
	/// owner-authoritative HeldItemKey write is legal. The prefab is cloned owner-
	/// locally; other clients don't have an instance, only the synced HeldItemKey.
	/// </summary>
	[Rpc.Owner]
	public void GrantItemRpc( string key, GameObject prefab )
	{
		if ( string.IsNullOrEmpty( key ) ) return;
		if ( !prefab.IsValid() )
		{
			if ( DebugLog ) Log.Warning( $"[PlayerItemTracker] GrantItemRpc('{key}') with invalid prefab — ignoring." );
			return;
		}
		if ( HasItem || heldInstance.IsValid() )
		{
			if ( DebugLog ) Log.Warning( $"[PlayerItemTracker] GrantItemRpc('{key}') while already holding '{HeldItemKey}' — ignoring." );
			return;
		}

		heldInstance = prefab.Clone();
		if ( !heldInstance.IsValid() )
		{
			if ( DebugLog ) Log.Warning( $"[PlayerItemTracker] Clone of prefab for '{key}' failed." );
			return;
		}

		heldInstance.SetParent( ItemHost.IsValid() ? ItemHost : GameObject, false );
		heldInstance.Name = $"Power_{key}";

		heldPower = heldInstance.Components.Get<GodPower>( FindMode.EverythingInSelfAndDescendants );
		if ( heldPower is null )
		{
			Log.Warning( $"[PlayerItemTracker] Prefab for '{key}' has no GodPower component. Destroying clone." );
			heldInstance.Destroy();
			heldInstance = null;
			return;
		}

		heldPower.Owner = PlayerRoot.IsValid() ? PlayerRoot : FindRoot( GameObject );

		HeldItemKey = key;
		OnItemGranted?.Invoke( key, heldPower );

		if ( DebugLog )
			Log.Info( $"[PlayerItemTracker] Granted '{key}' -> spawned {heldPower.GetType().Name} under {heldInstance.Parent?.Name}." );
	}

	protected override void OnUpdate()
	{
		if ( Network.IsProxy ) return;
		if ( heldPower is null ) return;
		if ( string.IsNullOrEmpty( UseAction ) ) return;
		if ( !Input.Pressed( UseAction ) ) return;

		bool ultimate = !string.IsNullOrEmpty( UltimateModifierAction )
			&& Input.Down( UltimateModifierAction );

		if ( DebugLog )
			Log.Info( $"[PlayerItemTracker] UseAction '{UseAction}' pressed. Ultimate={ultimate}. HeldItemKey='{HeldItemKey}'." );

		UseHeldItem( ultimate );
	}

	/// <summary>
	/// Fire the held power (Normal or Ultimate). On success, mark the instance for
	/// destruction after its linger duration. The slot stays occupied until the
	/// linger elapses so the player can't double-dip during a still-running effect.
	/// </summary>
	public void UseHeldItem( bool ultimate )
	{
		if ( Network.IsProxy ) return;
		if ( heldPower is null ) return;

		var key = HeldItemKey;
		bool ok = ultimate ? heldPower.TryActivateUltimate() : heldPower.TryActivate();
		if ( !ok )
		{
			if ( DebugLog )
				Log.Warning( $"[PlayerItemTracker] TryActivate{(ultimate ? "Ultimate" : "")} returned false on {heldPower.GetType().Name}. IsSpent={heldPower.IsSpent}." );
			return;
		}

		OnItemUsed?.Invoke( key, heldPower );

		// Capture the instance reference so a future grant during the linger
		// can't accidentally destroy the wrong object.
		float linger = heldPower.ActiveLingerDuration;
		var instanceToDestroy = heldInstance;
		heldInstance = null;
		heldPower = null;

		if ( linger > 0f )
			Invoke( linger, () => FinishUse( instanceToDestroy, key ) );
		else
			FinishUse( instanceToDestroy, key );

		if ( DebugLog )
			Log.Info( $"[PlayerItemTracker] Used '{key}' (ult={ultimate}). Linger={linger}s." );
	}

	private void FinishUse( GameObject instance, string key )
	{
		if ( instance.IsValid() )
			instance.Destroy(); // OnDisabled on each GodPower subclass reverts any pending effect.

		// Only clear if a NEW item wasn't granted during the linger (rare race).
		if ( HeldItemKey == key )
			HeldItemKey = "";

		if ( DebugLog )
			Log.Info( $"[PlayerItemTracker] Finalized '{key}' — instance destroyed, slot clear." );
	}

	private static GameObject FindRoot( GameObject from )
	{
		var node = from;
		while ( node.IsValid() && node.Parent.IsValid() )
			node = node.Parent;
		return node;
	}
}
