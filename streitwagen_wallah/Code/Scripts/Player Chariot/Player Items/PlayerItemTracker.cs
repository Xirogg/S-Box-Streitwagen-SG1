using Sandbox;
using System;
using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Mario-Kart-style item slot for one player. Holds at most one GodPower
/// "ticket" at a time. When the owner presses UseAction, the held power's
/// TryActivate() is called and the slot empties.
///
/// Item pool:
///   ItemPool is a Dictionary&lt;string, GodPower&gt;. The KEY is a free-form
///   id you choose ("grapes", "shield", "lightning", ...). The VALUE is the
///   GodPower component on this player that the key maps to. To control which
///   items can drop, add or remove entries in the inspector — pickups randomly
///   pick one of the dictionary keys.
///
/// Networking:
///   - Lives on the player chariot prefab (owner-owned).
///   - HeldItemKey is [Sync] (owner-authoritative) so other clients can read
///     it for UI / observation.
///   - GrantItemRpc is [Rpc.Owner]: the host's ItemPrefab calls it on pickup,
///     it runs on the owning client, which writes HeldItemKey, which then
///     replicates back out via [Sync].
///
/// Setup:
///   1. Attach to the player chariot prefab.
///   2. Fill ItemPool with key → GodPower entries (drag GodPower components
///      already attached to this player in as the values).
///   3. Add an input action "UseItem" in Project Settings → Input, or change
///      UseAction to whatever you have bound.
///   4. If you want powers to be ONLY usable via item pickup, clear the
///      ActivationAction field on each GodPower component in the prefab.
/// </summary>
public sealed class PlayerItemTracker : Component
{
	/// <summary>
	/// Available items, keyed by id. The id is what gets synced on pickup.
	/// Removing an entry takes that power out of the random pool.
	/// </summary>
	[Property, Group( "Items" )]
	public Dictionary<string, GodPower> ItemPool { get; set; } = new();

	/// <summary>Input action that consumes the held item.</summary>
	[Property, Group( "Input" )]
	public string UseAction { get; set; } = "UseItem";

	/// <summary>Empty string = no item held. Otherwise a key into ItemPool. Replicated.</summary>
	[Sync] public string HeldItemKey { get; set; } = "";

	public bool HasItem =>
		!string.IsNullOrEmpty( HeldItemKey ) && ItemPool.ContainsKey( HeldItemKey );

	public GodPower HeldPower => HasItem ? ItemPool[HeldItemKey] : null;

	/// <summary>Owner-side event. Fires when an item is granted. Useful for HUD / SFX.</summary>
	public event Action<string, GodPower> OnItemGranted;

	/// <summary>Owner-side event. Fires when the held item is consumed.</summary>
	public event Action<string, GodPower> OnItemUsed;

	/// <summary>
	/// Called by the host's ItemPrefab on pickup. Runs on the owning client so
	/// the owner-authoritative [Sync] write to HeldItemKey is legal.
	/// </summary>
	[Rpc.Owner]
	public void GrantItemRpc( string key )
	{
		if ( string.IsNullOrEmpty( key ) ) return;
		if ( !ItemPool.ContainsKey( key ) ) return;
		if ( HasItem ) return; // Defensive: host already checked, but races happen.

		HeldItemKey = key;
		OnItemGranted?.Invoke( key, HeldPower );
	}

	protected override void OnUpdate()
	{
		// Only the owning client reads input and drives item use.
		if ( Network.IsProxy ) return;
		if ( !HasItem ) return;
		if ( string.IsNullOrEmpty( UseAction ) ) return;
		if ( !Input.Pressed( UseAction ) ) return;

		UseHeldItem();
	}

	/// <summary>
	/// Consume the held item. Delegates the actual effect to the existing
	/// GodPower.TryActivate() path (cooldown, CanActivate, OnActivate). On
	/// success, the slot empties.
	/// </summary>
	public void UseHeldItem()
	{
		if ( Network.IsProxy ) return;

		var key = HeldItemKey;
		var power = HeldPower;
		if ( power is null ) return;
		if ( !power.TryActivate() ) return;

		OnItemUsed?.Invoke( key, power );
		HeldItemKey = "";
	}
}
