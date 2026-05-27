using Sandbox;
using System;
using System.Linq;

namespace Sandbox;

/// <summary>
/// Mario-Kart-style item box. Place instances of this prefab around the level
/// as item spawn locations. When a player's collider enters the trigger and
/// the player isn't already holding something, the host picks a random key
/// from that player's PlayerItemTracker.ItemPool and grants it. The box then
/// hides for a random duration in [MinRespawnSeconds, MaxRespawnSeconds].
///
/// Networking:
///   - Scene-placed (host-owned). Available is [Sync] from host.
///   - OnTriggerEnter fires on every client; only the host processes pickups.
///   - Host calls [Rpc.Owner] GrantItemRpc on the player's tracker.
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

		// Mario-Kart rule: if this player is already holding an item, leave the
		// box completely untouched — it stays available on the ground for the
		// next player. We intentionally return BEFORE flipping Available.
		if ( tracker.HasItem ) return;
		if ( tracker.ItemPool.Count == 0 ) return;

		// Uniform-random pick from the configured pool keys.
		string key = tracker.ItemPool.Keys.ElementAt(
			Random.Shared.Next( 0, tracker.ItemPool.Count ) );
		tracker.GrantItemRpc( key );

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
