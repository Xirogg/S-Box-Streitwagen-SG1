using Sandbox;
using System;

namespace Sandbox;

/// <summary>
/// Mario-Kart-style item box. Place instances of this prefab around the level
/// as item spawn locations. When a player's collider enters the trigger, the
/// host grants that player a random GodPower from their PlayerItemTracker.ItemPool,
/// then hides the box for a random duration in [MinRespawnSeconds, MaxRespawnSeconds].
///
/// Networking:
///   - This component lives on a scene-placed (host-owned) GameObject.
///   - Available is [Sync] from host → all clients render/collide consistently.
///   - OnTriggerEnter fires on every client; only the host actually processes pickups.
///   - The host calls a [Rpc.Owner] method on the player's tracker to hand them the item.
/// </summary>
public sealed class ItemPrefab : Component, Component.ITriggerListener
{
	[Property, Group( "Visuals" )]
	public ModelRenderer Renderer { get; set; }

	[Property, Group( "Visuals" )]
	public Collider Trigger { get; set; }

	[Property, Group( "Respawn" )]
	public float MinRespawnSeconds { get; set; } = 20f;

	[Property, Group( "Respawn" )]
	public float MaxRespawnSeconds { get; set; } = 40f;

	/// <summary>True = box is pickupable and visible. Host-authoritative, replicated.</summary>
	[Sync] public bool Available { get; set; } = true;

	protected override void OnStart()
	{
		// Auto-wire references if not assigned in the inspector.
		if ( !Renderer.IsValid() )
			Renderer = Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants );

		if ( !Trigger.IsValid() )
			Trigger = Components.Get<Collider>( FindMode.EverythingInSelfAndDescendants );
	}

	protected override void OnUpdate()
	{
		// Mirror the synced Available flag onto the visual + collider so every
		// client sees the same state. Cheap idempotent toggles.
		if ( Renderer.IsValid() && Renderer.Enabled != Available )
			Renderer.Enabled = Available;

		if ( Trigger.IsValid() && Trigger.Enabled != Available )
			Trigger.Enabled = Available;
	}

	void Component.ITriggerListener.OnTriggerEnter( Collider other )
	{
		// Only the host decides who gets the item — avoids double-grants.
		if ( !Networking.IsHost ) return;
		if ( !Available ) return;

		var tracker = other.GameObject.Components.Get<PlayerItemTracker>(
			FindMode.EverythingInSelfAndAncestors );
		if ( tracker is null ) return;
		if ( tracker.HasItem ) return;            // Mario Kart: only one item at a time.
		if ( tracker.ItemPool.Count == 0 ) return; // Nothing to give.

		int index = Random.Shared.Next( 0, tracker.ItemPool.Count );
		tracker.GrantItemRpc( index );

		Available = false;
		float wait = Random.Shared.Float( MinRespawnSeconds, MaxRespawnSeconds );
		Invoke( wait, RespawnItem );
	}

	void Component.ITriggerListener.OnTriggerExit( Collider other ) { }

	private void RespawnItem()
	{
		if ( !Networking.IsHost ) return;
		Available = true;
	}
}
