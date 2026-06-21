using Sandbox;
using System;
using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Randomized sound feedback for item pickups and god-power use.
///
/// Drop this component on the player prefab (or a child of it). On start it walks
/// up to the player root, finds the <see cref="PlayerItemTracker"/> anywhere in
/// that tree, and subscribes to its <c>OnItemGranted</c> (pickup) and
/// <c>OnItemUsed</c> (god power used) events. Both share the same <see cref="Sounds"/>
/// pool: each time one fires it picks a random <see cref="SoundEvent"/> and plays
/// it at the player's position with a slightly randomized pitch and a CONSTANT volume.
///
/// Multiplayer:
///   - PlayerItemTracker's pickup/use events only fire on the OWNING client, so
///     only the owner reacts here — there is no double-trigger across clients.
///   - With <see cref="PlayForEveryone"/> on (default), the owner broadcasts the
///     chosen sound index + pitch over an RPC so every client plays the *same*
///     file at the *same* pitch, positioned at the player. Distance attenuation
///     on the SoundEvent keeps far-away events quiet.
///   - In the editor / single-player (no active network session) it just plays
///     locally, so it always works while testing.
/// </summary>
public sealed class ItemSoundPlayer : Component
{
	/// <summary>
	/// Random sound pool, shared by both pickup and use. Drag your .sound assets
	/// here; one is chosen at random each time.
	/// </summary>
	[Property, Group( "Sounds" )]
	public List<SoundEvent> Sounds { get; set; } = new();

	/// <summary>Constant playback volume. Only the pitch is randomized, not this.</summary>
	[Property, Group( "Playback" ), Range( 0f, 2f )]
	public float Volume { get; set; } = 1f;

	/// <summary>Lower bound of the random pitch (1 = the file's original pitch).</summary>
	[Property, Group( "Playback" ), Range( 0.1f, 2f )]
	public float PitchMin { get; set; } = 0.9f;

	/// <summary>Upper bound of the random pitch (1 = the file's original pitch).</summary>
	[Property, Group( "Playback" ), Range( 0.1f, 2f )]
	public float PitchMax { get; set; } = 1.1f;

	/// <summary>
	/// True (default): broadcast so every player hears the sound at this player's
	/// position. False: only the local owner hears it.
	/// </summary>
	[Property, Group( "Multiplayer" )]
	public bool PlayForEveryone { get; set; } = true;

	/// <summary>
	/// World position the sound plays from. Defaults to this GameObject. Point it
	/// at the chariot body if you want the sound centered there.
	/// </summary>
	[Property, Group( "Multiplayer" )]
	public GameObject SoundOrigin { get; set; }

	/// <summary>
	/// Optional explicit tracker. Leave null to auto-find the player's
	/// PlayerItemTracker on start.
	/// </summary>
	[Property, Group( "Wiring" )]
	public PlayerItemTracker Tracker { get; set; }

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = false;

	/// <summary>DEBUG: when on, auto-plays a random sound every <see cref="DebugInterval"/> seconds. Turn off for normal play.</summary>
	[Property, Group( "Debug" )]
	public bool DebugAutoPlay { get; set; } = false;

	/// <summary>DEBUG: seconds between auto-plays while <see cref="DebugAutoPlay"/> is on.</summary>
	[Property, Group( "Debug" ), Range( 0.1f, 30f )]
	public float DebugInterval { get; set; } = 5f;

	private float debugTimer;

	protected override void OnStart()
	{
		if ( !SoundOrigin.IsValid() )
			SoundOrigin = GameObject;

		if ( !Tracker.IsValid() )
			Tracker = FindTracker();

		if ( !Tracker.IsValid() )
		{
			Log.Warning( "[ItemSoundPlayer] No PlayerItemTracker found on the player — no pickup/use sounds will play. Assign one in the inspector." );
			return;
		}

		Tracker.OnItemGranted += HandleItemGranted;
		Tracker.OnItemUsed += HandleItemUsed;

		if ( DebugLog )
			Log.Info( $"[ItemSoundPlayer] Subscribed to tracker on '{Tracker.GameObject?.Name}'." );
	}

	protected override void OnDestroy()
	{
		// Always unsubscribe — the tracker outlives a disabled/destroyed sound player.
		if ( Tracker.IsValid() )
		{
			Tracker.OnItemGranted -= HandleItemGranted;
			Tracker.OnItemUsed -= HandleItemUsed;
		}
	}

	protected override void OnUpdate()
	{
		if ( !DebugAutoPlay ) return;
		// Only the owner drives the timer, mirroring the real pickup/use path so a
		// live session doesn't get one broadcast per client every tick.
		if ( Network.IsProxy ) return;

		debugTimer += Time.Delta;
		if ( debugTimer < DebugInterval ) return;

		debugTimer = 0f;
		Trigger();
	}

	private void HandleItemGranted( string key, GodPower power ) => Trigger();
	private void HandleItemUsed( string key, GodPower power ) => Trigger();

	/// <summary>
	/// Pick a random sound + pitch and play it. Runs on the owning client only
	/// (the tracker's events never fire on proxies), so the random choice is made
	/// once and then shared with everyone else.
	/// </summary>
	private void Trigger()
	{
		if ( !Active ) return;

		if ( Sounds is null || Sounds.Count == 0 )
		{
			if ( DebugLog )
				Log.Info( "[ItemSoundPlayer] Sounds list is empty — nothing to play." );
			return;
		}

		int index = Random.Shared.Next( 0, Sounds.Count );
		float pitch = Random.Shared.Float( PitchMin, PitchMax );

		// Broadcast in a live session so all clients hear the same sound; play
		// locally otherwise so it still works in the editor / single-player.
		if ( PlayForEveryone && Networking.IsActive )
			PlaySoundRpc( index, pitch );
		else
			PlaySoundLocal( index, pitch );
	}

	/// <summary>Broadcasts the chosen sound to every client so all players hear it.</summary>
	[Rpc.Broadcast]
	private void PlaySoundRpc( int index, float pitch )
	{
		PlaySoundLocal( index, pitch );
	}

	private void PlaySoundLocal( int index, float pitch )
	{
		if ( Sounds is null || index < 0 || index >= Sounds.Count )
			return;

		var ev = Sounds[index];
		if ( ev is null )
			return;

		var origin = SoundOrigin.IsValid() ? SoundOrigin : GameObject;
		var handle = Sound.Play( ev, origin.Transform.Position );
		if ( handle is null )
			return;

		handle.Volume = Volume; // constant volume
		handle.Pitch = pitch;   // randomized per play

		if ( DebugLog )
			Log.Info( $"[ItemSoundPlayer] Playing sound #{index} '{ev.ResourcePath}' pitch={pitch:0.00}." );
	}

	/// <summary>Walk up to the player root, then search the whole tree for the tracker.</summary>
	private PlayerItemTracker FindTracker()
	{
		var root = GameObject;
		while ( root.Parent.IsValid() )
			root = root.Parent;
		return root.Components.Get<PlayerItemTracker>( FindMode.EverythingInSelfAndDescendants );
	}
}
