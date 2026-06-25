using Sandbox;
using System;

namespace Sandbox;

/// <summary>
/// Plays the item-PICKUP jingle: a fixed two-clip sequence (Sound 1 → Sound 2) that
/// gates the actual item grant — the player only receives the item once Sound 2 ends.
///
/// Drop this on the player prefab (or a child). PlayerItemTracker finds it on pickup
/// and calls <see cref="PlayPickupSequence"/> directly; this component no longer
/// subscribes to any tracker events.
///
/// The old random "chonk" pool that used to fire on pickup AND on god-power use has
/// been removed: pickup is this dedicated sequence, and god-power pickup/use sounds now
/// come from the GodPowers SFX modules (GodPowersNormalSfxmodule / …UltimateSfxmodule).
///
/// Multiplayer:
///   - Only the OWNING client runs the sequence timer; each clip is broadcast so every
///     client hears the same file at the same pitch, positioned at the player.
///   - In the editor / single-player (no active session) it just plays locally.
/// </summary>
public sealed class ItemSoundPlayer : Component
{
	/// <summary>
	/// First clip of the pickup jingle. Played the instant the player drives into an
	/// item box. Drag a .sound asset here in the editor. Leave empty to skip straight
	/// to <see cref="PickupSound2"/>.
	/// </summary>
	[Property, Group( "Pickup Sequence" )]
	public SoundEvent PickupSound1 { get; set; }

	/// <summary>
	/// Second clip of the pickup jingle. Played only AFTER <see cref="PickupSound1"/>
	/// has finished. The item is granted once THIS clip finishes — so the two sounds
	/// together gate (delay) the moment the player actually receives the item.
	/// </summary>
	[Property, Group( "Pickup Sequence" )]
	public SoundEvent PickupSound2 { get; set; }

	/// <summary>
	/// Safety cap (seconds) for each pickup clip. If a clip never reports Finished
	/// (missing asset, looping sound, …) the sequence advances anyway after this long,
	/// so the player is guaranteed to eventually get the item.
	/// </summary>
	[Property, Group( "Pickup Sequence" ), Range( 0.25f, 15f )]
	public float MaxClipSeconds { get; set; } = 8f;

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
	/// True (default): play positioned at the player (3D — attenuates with distance).
	/// False: play as a 2D sound at full volume regardless of listener position.
	/// Flip this to False if 3D playback seems silent — it isolates listener/distance
	/// problems from "the clip isn't wired up" problems.
	/// </summary>
	[Property, Group( "Playback" )]
	public bool Spatial { get; set; } = true;

	/// <summary>
	/// True (default): broadcast so every player hears the jingle at this player's
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

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = false;

	/// <summary>DEBUG: when on, auto-plays the pickup jingle every <see cref="DebugInterval"/> seconds (no item granted). Turn off for normal play.</summary>
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
	}

	protected override void OnUpdate()
	{
		// Only the owner drives timing, so a live session doesn't get one broadcast
		// per client every tick.
		if ( Network.IsProxy ) return;

		// Advance the gated pickup sequence (plays clip 2 after clip 1, grants after clip 2).
		TickPickupSequence();

		if ( !DebugAutoPlay ) return;

		debugTimer += Time.Delta;
		if ( debugTimer < DebugInterval ) return;

		debugTimer = 0f;
		// Audition the pickup jingle (clip 1 → clip 2) with NO item granted.
		PlayPickupSequence( null );
	}

	/// <summary>
	/// DEBUG hook for ItemPrefab's sound-test mode. The host's ItemPrefab calls this
	/// as an [Rpc.Owner] so it runs on the OWNING client, which plays the pickup jingle
	/// without granting an item.
	/// </summary>
	[Rpc.Owner]
	public void PlayPickupSoundDebugRpc()
	{
		Log.Info( "[ItemSoundPlayer] PlayPickupSoundDebugRpc received — playing pickup sequence (no grant)." );
		PlayPickupSequence( null );
	}

	// ───────────────────────────── Pickup sequence ─────────────────────────────
	// Plays PickupSound1, waits for it to finish, plays PickupSound2, waits for IT to
	// finish, then runs the onComplete callback (the item grant). Only the owning
	// client runs this state machine; each clip is broadcast so everyone hears it.

	private enum PickupStage { Idle, Clip1, Clip2 }
	private PickupStage pickupStage = PickupStage.Idle;
	private SoundHandle pickupHandle;   // owner-local handle of the clip we're waiting on
	private float pickupStageTime;      // seconds spent in the current clip (safety cap)
	private Action pickupOnComplete;    // grant callback, fired after clip 2 finishes

	/// <summary>
	/// Start the two-clip pickup jingle. When <see cref="PickupSound2"/> finishes,
	/// <paramref name="onComplete"/> runs — that's where the tracker actually grants
	/// the item, so the sounds delay the pickup. A missing clip is skipped; a per-clip
	/// safety cap guarantees onComplete always fires. Call on the OWNING client.
	/// </summary>
	public void PlayPickupSequence( Action onComplete )
	{
		if ( pickupStage != PickupStage.Idle )
		{
			// A sequence is already mid-flight (shouldn't happen — the box gates on
			// PickupPending). Don't drop the item: just grant immediately.
			if ( DebugLog ) Log.Warning( "[ItemSoundPlayer] PlayPickupSequence called while one is running — granting now." );
			onComplete?.Invoke();
			return;
		}

		pickupOnComplete = onComplete;
		BeginPickupClip( PickupStage.Clip1, PickupSound1 );
	}

	private void BeginPickupClip( PickupStage stage, SoundEvent ev )
	{
		pickupStage = stage;
		pickupStageTime = 0f;
		pickupHandle = null;

		if ( ev is null )
		{
			// Empty slot — leave pickupHandle null so TickPickupSequence advances next frame.
			if ( DebugLog ) Log.Info( $"[ItemSoundPlayer] Pickup {stage}: no sound assigned, skipping." );
			return;
		}

		float pitch = Random.Shared.Float( PitchMin, PitchMax );
		int which = stage == PickupStage.Clip1 ? 1 : 2;

		// Broadcast in a live session so all clients hear it; play locally otherwise.
		// Either way the call sets pickupHandle on THIS client so we can poll Finished.
		if ( PlayForEveryone && Networking.IsActive )
			PlayPickupClipRpc( which, pitch );
		else
			pickupHandle = PlayPickupClipLocal( which, pitch );
	}

	private void TickPickupSequence()
	{
		if ( pickupStage == PickupStage.Idle ) return;

		pickupStageTime += Time.Delta;

		// Done when the clip reports Finished, was skipped (null handle), or we hit the cap.
		bool clipDone = pickupHandle is null || pickupHandle.Finished || pickupStageTime >= MaxClipSeconds;
		if ( !clipDone ) return;

		if ( pickupStage == PickupStage.Clip1 )
		{
			BeginPickupClip( PickupStage.Clip2, PickupSound2 );
			return;
		}

		// Clip 2 finished → end the sequence and grant the item.
		pickupStage = PickupStage.Idle;
		pickupHandle = null;
		var cb = pickupOnComplete;
		pickupOnComplete = null;

		if ( DebugLog ) Log.Info( "[ItemSoundPlayer] Pickup sequence complete → granting item." );
		cb?.Invoke();
	}

	/// <summary>Broadcasts a pickup clip (1 or 2) so every client plays it; each client
	/// resolves the index against its own PickupSound1/PickupSound2 so no SoundEvent is
	/// sent over the wire.</summary>
	[Rpc.Broadcast]
	private void PlayPickupClipRpc( int which, float pitch )
	{
		pickupHandle = PlayPickupClipLocal( which, pitch );
	}

	private SoundHandle PlayPickupClipLocal( int which, float pitch )
	{
		var ev = which == 1 ? PickupSound1 : PickupSound2;
		if ( ev is null ) return null;

		var origin = SoundOrigin.IsValid() ? SoundOrigin : GameObject;
		var handle = Spatial
			? Sound.Play( ev, origin.WorldPosition ) // 3D at the player
			: Sound.Play( ev );                       // 2D, full volume everywhere
		if ( handle is null )
		{
			Log.Warning( $"[ItemSoundPlayer] Pickup clip {which}: Sound.Play returned null for '{ev.ResourcePath}'." );
			return null;
		}

		handle.Volume = Volume;
		handle.Pitch = pitch;

		// Pin a 3D clip to the chariot so it travels with it instead of lingering at the box.
		if ( Spatial && origin.IsValid() )
		{
			handle.Parent = origin;
			handle.FollowParent = true;
		}

		if ( DebugLog )
			Log.Info( $"[ItemSoundPlayer] Pickup clip {which} '{ev.ResourcePath}' spatial={Spatial} vol={Volume} pitch={pitch:0.00}." );
		return handle;
	}
}
