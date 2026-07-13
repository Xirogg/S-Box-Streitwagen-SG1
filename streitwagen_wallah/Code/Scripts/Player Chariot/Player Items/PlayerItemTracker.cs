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
///      ItemHost, sets the GodPower's Owner to PlayerRoot, rolls the Normal/Ultimate
///      variant into GodPower.IsUltimate, and writes HeldItemKey ([Sync], so other
///      clients' HUDs update too).
///   3. Owner presses UseAction. Whether the Normal or Ultimate ability runs is
///      decided by the held power's IsUltimate flag, not by the player. TryActivate
///      runs the effect, flips IsSpent.
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

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = true;

	/// <summary>Empty = no item held. UI reads this; replicated owner → others.</summary>
	[Sync] public string HeldItemKey { get; set; } = "";

	/// <summary>
	/// True while a pickup's sound sequence is playing but the item hasn't been granted
	/// yet. [Sync] so the host's ItemPrefab sees it too and won't let a SECOND box start
	/// granting during the jingle. Owner-written.
	/// </summary>
	[Sync] public bool PickupPending { get; set; }

	// Owner-side runtime — the cloned instance only exists on the owning client.
	private GameObject heldInstance;
	private GodPower heldPower;

	/// <summary>
	/// The source prefab that produced <see cref="heldInstance"/>. Cached so that
	/// powers like Laverna's Item-Dieb can transfer an item to another tracker
	/// without needing to know the original item pool — we hand the same prefab
	/// to the recipient's GrantItemRpc.
	/// </summary>
	private GameObject heldPrefab;

	/// <summary>The prefab the current held item was cloned from. Null if none held.</summary>
	public GameObject HeldPrefab => heldPrefab;

	/// <summary>
	/// True while an item is held — including the linger phase after activation,
	/// so the box rejects a new pickup until the previous use fully cleans up.
	/// </summary>
	public bool HasItem => !string.IsNullOrEmpty( HeldItemKey );

	/// <summary>
	/// True if the slot is occupied OR a pickup jingle is mid-flight. The item box
	/// checks this so a power can't be granted twice while the sounds are still playing.
	/// </summary>
	public bool IsBusy => HasItem || PickupPending;

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
				heldPrefab = null;
			}
		}
	}

	protected override void OnDestroy()
	{
		if ( heldInstance.IsValid() )
			heldInstance.Destroy();
	}

	/// <summary>
	/// Pickup entry point used by the item box. Runs on the OWNING client. Plays the
	/// ItemSoundPlayer's two-clip jingle (Sound 1 → Sound 2) FIRST and only grants the
	/// item once the second clip finishes — so the sounds delay the pickup. Falls back
	/// to an instant grant if there's no ItemSoundPlayer on the player.
	/// </summary>
	[Rpc.Owner]
	public void BeginPickupSequenceRpc( string key, GameObject prefab, bool isUltimate )
	{
		if ( !CanGrant( key, prefab ) ) return;

		var sfx = FindSoundPlayer();
		if ( sfx is null )
		{
			if ( DebugLog ) Log.Info( "[PlayerItemTracker] No ItemSoundPlayer found — granting instantly (no pickup jingle)." );
			DoGrant( key, prefab, isUltimate );
			return;
		}

		// Mark busy so a second box hit during the jingle can't also grant. [Sync] so
		// the host's ItemPrefab sees it. Cleared inside DoGrant when the sequence ends.
		PickupPending = true;
		if ( DebugLog ) Log.Info( $"[PlayerItemTracker] Pickup '{key}' (ult={isUltimate}) — playing sound sequence, granting after it finishes." );

		sfx.PlayPickupSequence( () => DoGrant( key, prefab, isUltimate ) );
	}

	/// <summary>
	/// Instant grant with NO pickup sounds. Used by Laverna's item-steal transfer,
	/// which hands an already-owned power to another player and shouldn't replay the
	/// pickup jingle. Runs on the OWNING client.
	/// </summary>
	[Rpc.Owner]
	public void GrantItemRpc( string key, GameObject prefab, bool isUltimate )
	{
		if ( !CanGrant( key, prefab ) ) return;
		DoGrant( key, prefab, isUltimate );
	}

	/// <summary>Shared pre-grant validation for both the pickup and transfer paths.</summary>
	private bool CanGrant( string key, GameObject prefab )
	{
		if ( string.IsNullOrEmpty( key ) ) return false;
		if ( !prefab.IsValid() )
		{
			if ( DebugLog ) Log.Warning( $"[PlayerItemTracker] Grant('{key}') with invalid prefab — ignoring." );
			return false;
		}
		if ( IsBusy || heldInstance.IsValid() )
		{
			if ( DebugLog ) Log.Warning( $"[PlayerItemTracker] Grant('{key}') while busy (held='{HeldItemKey}', pending={PickupPending}) — ignoring." );
			return false;
		}
		return true;
	}

	/// <summary>
	/// Does the actual grant: clones the power owner-locally, wires its Owner, and
	/// publishes HeldItemKey. Always clears <see cref="PickupPending"/> so the slot
	/// can't get stuck busy. Re-checks validity because it may run after a sound delay.
	/// </summary>
	private void DoGrant( string key, GameObject prefab, bool isUltimate )
	{
		PickupPending = false;

		if ( !prefab.IsValid() )
		{
			if ( DebugLog ) Log.Warning( $"[PlayerItemTracker] DoGrant('{key}') — prefab went invalid during the pickup delay." );
			return;
		}
		if ( HasItem || heldInstance.IsValid() )
		{
			if ( DebugLog ) Log.Warning( $"[PlayerItemTracker] DoGrant('{key}') — already holding '{HeldItemKey}' after the delay, dropping." );
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

		// Lock in the variant rolled at pickup (or inherited from a stolen item). This
		// is what UseHeldItem reads to decide Normal vs Ultimate — the player can't pick.
		heldPower.IsUltimate = isUltimate;

		heldPrefab = prefab;
		HeldItemKey = key;
		OnItemGranted?.Invoke( key, heldPower );

		if ( DebugLog )
			Log.Info( $"[PlayerItemTracker] Granted '{key}' (ult={isUltimate}) -> spawned {heldPower.GetType().Name} under {heldInstance.Parent?.Name}." );
	}

	protected override void OnUpdate()
	{
		if ( Network.IsProxy ) return;
		if ( heldPower is null ) return;
		if ( string.IsNullOrEmpty( UseAction ) ) return;
		if ( !Input.Pressed( UseAction ) ) return;

		if ( DebugLog )
			Log.Info( $"[PlayerItemTracker] UseAction '{UseAction}' pressed. Ultimate={heldPower.IsUltimate}. HeldItemKey='{HeldItemKey}'." );

		UseHeldItem();
	}

	/// <summary>
	/// Fire the held power. Whether the Normal or Ultimate ability runs is decided by
	/// the held power's IsUltimate flag (rolled at pickup), not by the player. On
	/// success, mark the instance for destruction after its linger duration. The slot
	/// stays occupied until the linger elapses so the player can't double-dip during a
	/// still-running effect.
	/// </summary>
	public void UseHeldItem()
	{
		if ( Network.IsProxy ) return;
		if ( heldPower is null ) return;

		var key = HeldItemKey;
		bool ultimate = heldPower.IsUltimate;
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
		heldPrefab = null;

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
		{
			HeldItemKey = "";
			heldPrefab = null;
		}

		if ( DebugLog )
			Log.Info( $"[PlayerItemTracker] Finalized '{key}' — instance destroyed, slot clear." );
	}

	/// <summary>
	/// Laverna's Item-Dieb hook. Runs on the VICTIM's owner client: pops the
	/// current held item out of this tracker, then hands the same (key, prefab)
	/// to the recipient via the regular GrantItemRpc path. The recipient's
	/// GrantItemRpc will be rejected if their slot is still occupied, so the
	/// thief side schedules this call AFTER its own use-cleanup has run.
	/// </summary>
	[Rpc.Owner]
	public void TransferHeldItemRpc( PlayerItemTracker recipient )
	{
		if ( !HasItem )
		{
			if ( DebugLog ) Log.Info( "[PlayerItemTracker] TransferHeldItemRpc called but slot is empty — nothing to steal." );
			return;
		}
		if ( !heldPrefab.IsValid() )
		{
			if ( DebugLog ) Log.Warning( $"[PlayerItemTracker] TransferHeldItemRpc: no cached prefab for '{HeldItemKey}' — can't transfer." );
			return;
		}
		if ( !recipient.IsValid() )
		{
			if ( DebugLog ) Log.Warning( "[PlayerItemTracker] TransferHeldItemRpc: recipient invalid." );
			return;
		}

		string stolenKey = HeldItemKey;
		var stolenPrefab = heldPrefab;
		// Carry the variant across the steal — robbing an Ultimate hands over an Ultimate.
		bool stolenUltimate = heldPower is not null && heldPower.IsUltimate;

		// Wipe victim slot — destroys their clone of the power so they lose access immediately.
		if ( heldInstance.IsValid() )
			heldInstance.Destroy();
		heldInstance = null;
		heldPower = null;
		heldPrefab = null;
		HeldItemKey = "";

		if ( DebugLog )
			Log.Info( $"[PlayerItemTracker] Robbed of '{stolenKey}' (ult={stolenUltimate}) by {recipient.GameObject?.Name} — forwarding to recipient." );

		// Routes to the recipient's owner client and clones the same prefab into their tracker.
		recipient.GrantItemRpc( stolenKey, stolenPrefab, stolenUltimate );
	}

	private static GameObject FindRoot( GameObject from )
	{
		var node = from;
		while ( node.IsValid() && node.Parent.IsValid() )
			node = node.Parent;
		return node;
	}

	/// <summary>
	/// Find the player's ItemSoundPlayer. It can live on a sibling branch, so we climb
	/// to the player root and search the whole subtree.
	/// </summary>
	private ItemSoundPlayer FindSoundPlayer()
	{
		var root = FindRoot( GameObject );
		return root.Components.Get<ItemSoundPlayer>( FindMode.EverythingInSelfAndDescendants );
	}
}
