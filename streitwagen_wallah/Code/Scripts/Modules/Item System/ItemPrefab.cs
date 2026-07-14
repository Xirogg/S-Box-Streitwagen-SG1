using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox;

/// <summary>
/// Mario-Kart-style item box. Place instances around the level as item spawns.
/// On pickup, the host picks a random key from ItemPool, then RPCs the entering
/// player's PlayerItemTracker to clone the matching GodPower prefab into the
/// player's tree. The box hides for a random duration in [Min..Max]RespawnSeconds.
///
/// The item pool lives HERE (not on the player) so the player prefab carries no
/// powers by default — they only exist on a player while they're holding the item.
///
/// Networking:
///   - Scene-placed (host-owned). Available is [Sync] from host.
///   - OnTriggerEnter fires on every client; only the authority (host, or solo in
///     editor) processes pickups via IsAuthority.
///   - Host calls [Rpc.Owner] GrantItemRpc on the player's tracker, which spawns
///     the cloned power owner-locally.
/// </summary>
public sealed class ItemPrefab : Component, Component.ITriggerListener
{
	/// <summary>
	/// The item pool for this box. Keys are free-form ids (e.g. "Dionysos", "Maat",
	/// "Laverna"). Values are GodPower PREFAB GameObjects — drag a prefab asset that
	/// has a GodPower-derived component on its root. The 2-letter abbreviation in the
	/// HUD comes from the key.
	/// </summary>
	[Property, Group( "Items" )]
	public Dictionary<string, GameObject> ItemPool { get; set; } = new();

	/// <summary>
	/// Chance (0..1) that a pickup from this box grants the Ultimate variant instead of
	/// the Normal one. Rolled per-pickup on the host and passed to the player's tracker,
	/// which locks it into the spawned GodPower's IsUltimate. Default 0.15 = 15%.
	/// </summary>
	[Property, Group( "Items" ), Range( 0f, 1f )]
	public float UltimateChance { get; set; } = 0.15f;

	[Property, Group( "Visuals" )]
	public ModelRenderer Renderer { get; set; }

	[Property, Group( "Visuals" )]
	public Collider Trigger { get; set; }

	[Property, Group( "Respawn" )]
	public float MinRespawnSeconds { get; set; } = 20f;

	[Property, Group( "Respawn" )]
	public float MaxRespawnSeconds { get; set; } = 40f;

	/// <summary>
	/// Tag carried by the player chariot's collider GameObject (e.g. "wagen"). The
	/// pickup search will refuse to grant the item unless the entering collider has
	/// this tag somewhere in its ancestor chain. This stops scene props (Floor,
	/// walls, debris) from accidentally collecting items when their colliders
	/// overlap a box at scene load.
	/// </summary>
	[Property, Group( "Items" )]
	public string WagenTag { get; set; } = "wagen";

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = true;

	/// <summary>
	/// DEBUG: when on, driving into this box plays the player's pickup sound and the
	/// box despawns/respawns as usual, but NO item is granted — so the player can
	/// keep hitting boxes to audition pickup sounds. Turn off for real gameplay.
	/// </summary>
	[Property, Group( "Debug" )]
	public bool SoundDebugMode { get; set; } = false;

	/// <summary>True = pickupable and visible. Host-authoritative, replicated.</summary>
	[Sync] public bool Available { get; set; } = true;

	/// <summary>
	/// True when this client is allowed to process pickups. Acts as host in a network
	/// session, or as the sole player in editor / single-player.
	/// </summary>
	private bool IsAuthority => !Networking.IsActive || Networking.IsHost;

	protected override void OnStart()
	{
		if ( !Renderer.IsValid() )
			Renderer = Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		if ( !Trigger.IsValid() )
			Trigger = Components.Get<Collider>( FindMode.EverythingInSelfAndDescendants );
	}

	protected override void OnUpdate()
	{
		if ( Renderer.IsValid() && Renderer.Enabled != Available )
			Renderer.Enabled = Available;
		if ( Trigger.IsValid() && Trigger.Enabled != Available )
			Trigger.Enabled = Available;
	}

	void Component.ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( DebugLog )
			Log.Info( $"[ItemPrefab] OnTriggerEnter from {other.GameObject?.Name} | IsAuthority={IsAuthority} Available={Available}" );

		if ( !IsAuthority ) return;
		if ( !Available ) return;

		var tracker = FindTrackerOnPlayer( other.GameObject );
		if ( tracker is null )
		{
			if ( DebugLog ) Log.Info( $"[ItemPrefab] {other.GameObject?.Name} has no '{WagenTag}'-tagged ancestor or no PlayerItemTracker — ignoring." );
			return;
		}

		if ( SoundDebugMode )
		{
			// Sound test: play the pickup sound on this player and despawn the box,
			// but DON'T grant an item — so the player can keep collecting boxes.
			var sfx = FindSoundPlayer( tracker );
			if ( sfx is not null )
				sfx.PlayPickupSoundDebugRpc();
			else if ( DebugLog )
				Log.Warning( "[ItemPrefab] SoundDebugMode is on but no ItemSoundPlayer was found on the player." );

			if ( DebugLog )
				Log.Info( "[ItemPrefab] SoundDebugMode: played pickup sound, no item granted, hiding box." );

			Available = false;
			Invoke( Random.Shared.Float( MinRespawnSeconds, MaxRespawnSeconds ), RespawnItem );
			return;
		}

		// Mario-Kart rule: if the player already holds something (or is still
		// lingering on a previous use, or is mid-pickup-jingle), the box stays for the
		// next player.
		if ( tracker.IsBusy )
		{
			if ( DebugLog ) Log.Info( "[ItemPrefab] Player already holds (or is picking up) an item, leaving box for next player." );
			return;
		}

		if ( ItemPool.Count == 0 )
		{
			if ( DebugLog ) Log.Warning( "[ItemPrefab] ItemPool is empty — fill it in the inspector." );
			return;
		}

		// Uniform-random pick.
		string key = ItemPool.Keys.ElementAt( Random.Shared.Next( 0, ItemPool.Count ) );
		if ( !ItemPool.TryGetValue( key, out var prefab ) || !prefab.IsValid() )
		{
			if ( DebugLog ) Log.Warning( $"[ItemPrefab] Pool entry '{key}' has no prefab." );
			return;
		}

		// Roll the variant on the host so it's authoritative and consistent with the
		// host-side key pick above. The player no longer chooses; the flag rides along to
		// the grant. Base chance is this box's UltimateChance plus any per-player bonus
		// (Laverna's Opferaltar level-3 "+5% ULT chance"), clamped to [0..1].
		float extraUlt = FindModifiersOnPlayer( tracker )?.ExtraUltimateChance ?? 0f;
		float ultChance = Math.Clamp( UltimateChance + extraUlt, 0f, 1f );
		bool isUltimate = Random.Shared.Float( 0f, 1f ) < ultChance;

		// Plays the pickup jingle (Sound 1 → Sound 2) on the player and grants the item
		// only after the second clip finishes — so the sound delays the grant.
		tracker.BeginPickupSequenceRpc( key, prefab, isUltimate );

		if ( DebugLog ) Log.Info( $"[ItemPrefab] Pickup '{key}' (ult={isUltimate}) by {other.GameObject?.Name} — sound sequence started, hiding box." );

		Available = false;
		float wait = Random.Shared.Float( MinRespawnSeconds, MaxRespawnSeconds );
		Invoke( wait, RespawnItem );
	}

	void Component.ITriggerListener.OnTriggerExit( Collider other ) { }

	private void RespawnItem()
	{
		if ( !IsAuthority ) return;
		Available = true;
	}

	/// <summary>
	/// Climb the parent chain of <paramref name="from"/>. As soon as we see a node
	/// tagged <see cref="WagenTag"/> we know this collider belongs to a real player
	/// (not the Floor or some scene prop). After that, keep climbing and at each
	/// ancestor level search its subtree for a PlayerItemTracker — that's needed
	/// because the tracker typically sits on a SIBLING branch of the Wagen node
	/// (e.g. Powers/PlayerItemTracker). Returns the first tracker found.
	///
	/// If the climb never sees the tag, we return null — that's what stops the
	/// scene-load Floor overlap from accidentally granting items.
	/// </summary>
	private PlayerItemTracker FindTrackerOnPlayer( GameObject from )
	{
		bool sawTag = false;
		var node = from;

		while ( node.IsValid() )
		{
			if ( !sawTag && node.Tags.Has( WagenTag ) )
				sawTag = true;

			if ( sawTag )
			{
				var t = node.Components.Get<PlayerItemTracker>( FindMode.EverythingInSelfAndDescendants );
				if ( t is not null ) return t;
			}

			node = node.Parent;
		}
		return null;
	}

	/// <summary>
	/// Resolve the entering player's <see cref="PlayerRaceModifiers"/> (may be null).
	/// Its <c>ExtraUltimateChance</c> is the Opferaltar's per-player ULT bonus. Found
	/// from the tracker's player root so it works regardless of where the component sits.
	/// </summary>
	private static PlayerRaceModifiers FindModifiersOnPlayer( PlayerItemTracker tracker )
	{
		if ( tracker is null ) return null;
		var root = tracker.PlayerRoot.IsValid() ? tracker.PlayerRoot : tracker.GameObject?.Root;
		return root?.Components.Get<PlayerRaceModifiers>( FindMode.EverythingInSelfAndDescendants );
	}

	/// <summary>
	/// From the player's tracker, climb to the player root and find its
	/// ItemSoundPlayer anywhere in the tree (it lives on a sibling branch).
	/// </summary>
	private ItemSoundPlayer FindSoundPlayer( PlayerItemTracker tracker )
	{
		var root = tracker.GameObject;
		while ( root.Parent.IsValid() )
			root = root.Parent;
		return root.Components.Get<ItemSoundPlayer>( FindMode.EverythingInSelfAndDescendants );
	}
}
