using Sandbox;
using System;
using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Mario-Kart-style item slot for one player. Holds at most one GodPower
/// "ticket" at a time. When the owner presses UseAction, the held power's
/// TryActivate() is called and the slot empties.
///
/// Networking:
///   - This component lives on the player chariot prefab (owner-owned).
///   - HeldPowerIndex is [Sync] (owner-authoritative) so other clients can
///     read it for UI / observation.
///   - GrantItemRpc is [Rpc.Owner], called by the host's ItemPrefab when a
///     pickup happens. The actual write to HeldPowerIndex therefore happens
///     on the owning client, which then replicates it out via [Sync].
///
/// Setup:
///   1. Drag the GodPower components attached to this player into ItemPool
///      (e.g. DionysosPower, LavernaGodPower, MaatGodPower). The index in
///      the list = the item id.
///   2. Add an input action named "UseItem" in Project Settings → Input,
///      or change UseAction to whatever you already have bound.
///   3. If you don't want powers to be activatable WITHOUT a pickup, clear
///      the ActivationAction on each GodPower component in the prefab.
/// </summary>
public sealed class PlayerItemTracker : Component
{
	/// <summary>
	/// Possible items this player can receive. References to GodPower components
	/// already attached to this player (discovered by PowerManager). The index in
	/// this list is the item id used by GrantItemRpc.
	/// </summary>
	[Property, Group( "Items" )]
	public List<GodPower> ItemPool { get; set; } = new();

	/// <summary>Input action that consumes the held item. Defined in Project Settings → Input.</summary>
	[Property, Group( "Input" )]
	public string UseAction { get; set; } = "UseItem";

	/// <summary>-1 = empty. Otherwise an index into ItemPool. Replicated to all clients.</summary>
	[Sync] public int HeldPowerIndex { get; set; } = -1;

	public bool HasItem => HeldPowerIndex >= 0 && HeldPowerIndex < ItemPool.Count;

	public GodPower HeldPower => HasItem ? ItemPool[HeldPowerIndex] : null;

	/// <summary>Fires on the owning client when an item is granted (use for HUD / SFX).</summary>
	public event Action<GodPower> OnItemGranted;

	/// <summary>Fires on the owning client when the held item is consumed.</summary>
	public event Action<GodPower> OnItemUsed;

	/// <summary>
	/// Called by the host's ItemPrefab on pickup. Runs on the owning client so
	/// the owner-authoritative [Sync] write to HeldPowerIndex is legal.
	/// </summary>
	[Rpc.Owner]
	public void GrantItemRpc( int index )
	{
		if ( index < 0 || index >= ItemPool.Count ) return;
		if ( HasItem ) return; // Defensive: host already checked, but races happen.

		HeldPowerIndex = index;
		OnItemGranted?.Invoke( HeldPower );
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
	/// Consume the held item. Calls TryActivate on the held GodPower —
	/// success/failure follows the existing cooldown/CanActivate rules in
	/// GodPower. On success, the slot empties.
	/// </summary>
	public void UseHeldItem()
	{
		if ( Network.IsProxy ) return;

		var power = HeldPower;
		if ( power is null ) return;
		if ( !power.TryActivate() ) return;

		OnItemUsed?.Invoke( power );
		HeldPowerIndex = -1;
	}
}
