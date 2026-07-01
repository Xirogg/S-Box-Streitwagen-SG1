using Sandbox;
using System;

namespace Sandbox;

/// <summary>
/// Proximity SFX for the horse:
///   - Move loop : Sound A loops while the horse is moving (speed above
///                 <see cref="MoveSpeedThreshold"/>); stops when it halts.
///   - Idle call : Sound B plays at a random interval (<see cref="MinInterval"/>..
///                 <see cref="MaxInterval"/>) with a slightly randomized pitch.
///
/// Put this on the horse node. The OWNING client decides when to start/stop the loop
/// and when to fire the random call, then broadcasts so everyone hears it positioned
/// at (and following) the horse.
///
/// Sound A is kept looping IN CODE: sbox has no loop flag on SoundHandle/SoundEvent,
/// so the clip is re-fired whenever it finishes while the horse is still moving, and
/// stopped when the horse halts. Author Sound A with matching start/end for a seamless
/// loop; otherwise expect a tiny gap each cycle.
/// </summary>
public sealed class ChariotHorseSfxmodule : Component
{
	[Property, Group( "Sounds" )] public SoundEvent MoveLoopSound { get; set; }   // A — looped while moving
	[Property, Group( "Sounds" )] public SoundEvent IdleRandomSound { get; set; } // B — random interval

	/// <summary>Speed above which the horse counts as "moving" (avoids loop flicker at rest).</summary>
	[Property, Group( "Movement" )] public float MoveSpeedThreshold { get; set; } = 5f;

	[Property, Group( "Idle Call" )] public float MinInterval { get; set; } = 10f;
	[Property, Group( "Idle Call" )] public float MaxInterval { get; set; } = 20f;
	[Property, Group( "Idle Call" ), Range( 0.1f, 2f )] public float PitchMin { get; set; } = 0.9f;
	[Property, Group( "Idle Call" ), Range( 0.1f, 2f )] public float PitchMax { get; set; } = 1.1f;

	[Property, Group( "Playback" ), Range( 0f, 2f )] public float Volume { get; set; } = 1f;

	/// <summary>World origin the 3D sounds emit from / follow. Defaults to this GameObject.</summary>
	[Property, Group( "Playback" )] public GameObject SoundOrigin { get; set; }

	[Property, Group( "Multiplayer" )] public bool PlayForEveryone { get; set; } = true;

	/// <summary>The horse Rigidbody. Auto-resolved from HorseController / ChariotPhysics if null.</summary>
	[Property, Group( "Wiring" )] public Rigidbody HorseBody { get; set; }

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = false;

	private bool loopActive;        // owner's intent: should the loop be playing?
	private SoundHandle loopHandle; // per-client handle for the running loop
	private float nextIdleAt;

	protected override void OnStart()
	{
		if ( !SoundOrigin.IsValid() )
			SoundOrigin = GameObject;

		if ( !HorseBody.IsValid() )
		{
			var horse = GameObject.Root?.Components.Get<HorseController>( FindMode.EverythingInSelfAndDescendants );
			HorseBody = horse?.Body
				?? GameObject.Root?.Components.Get<ChariotPhysics>( FindMode.EverythingInSelfAndDescendants )?.HorsePairRb
				?? Components.Get<Rigidbody>( FindMode.EverythingInSelfAndAncestors );
		}

		nextIdleAt = Time.Now + Random.Shared.Float( MinInterval, MaxInterval );
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;          // only the owner drives state & broadcasts
		if ( !HorseBody.IsValid() ) return;

		// Looped movement sound — start/stop on the moving/stopped edge.
		bool moving = HorseBody.Velocity.Length > MoveSpeedThreshold;
		if ( moving && !loopActive )
		{
			loopActive = true;
			StartLoop();
		}
		else if ( !moving && loopActive )
		{
			loopActive = false;
			StopLoop();
		}

		// Keep it ACTUALLY looping: the .sound may not be authored to loop, so whenever the
		// clip finishes while we still want it playing, fire it again. (Re-triggering is the
		// only reliable loop in s&box — there's no loop flag on SoundHandle/SoundEvent.)
		if ( loopActive && MoveLoopSound is not null && (loopHandle is null || loopHandle.Finished) )
			StartLoop();

		// Random idle call.
		if ( Time.Now >= nextIdleAt )
		{
			nextIdleAt = Time.Now + Random.Shared.Float( MinInterval, MaxInterval );
			PlayIdle( Random.Shared.Float( PitchMin, PitchMax ) );
		}
	}

	protected override void OnDisabled()
	{
		// Component switched off mid-gallop — make sure the loop doesn't hang.
		if ( loopActive )
		{
			loopActive = false;
			StopLoop();
		}
	}

	protected override void OnDestroy() => StopLoopLocal();

	// ───────── Move loop (networked start/stop) ─────────

	private void StartLoop()
	{
		if ( PlayForEveryone && Networking.IsActive )
			StartLoopRpc();
		else
			loopHandle = StartLoopLocal();
	}

	private void StopLoop()
	{
		if ( PlayForEveryone && Networking.IsActive )
			StopLoopRpc();
		else
			StopLoopLocal();
	}

	[Rpc.Broadcast] private void StartLoopRpc() => loopHandle = StartLoopLocal();
	[Rpc.Broadcast] private void StopLoopRpc() => StopLoopLocal();

	private SoundHandle StartLoopLocal()
	{
		StopLoopLocal(); // never stack two loops

		if ( MoveLoopSound is null )
		{
			if ( DebugLog ) Log.Warning( "[HorseSfx] MoveLoopSound not assigned." );
			return null;
		}

		var origin = SoundOrigin.IsValid() ? SoundOrigin : GameObject;
		var handle = Sound.Play( MoveLoopSound, origin.WorldPosition ); // 3D
		if ( handle is null ) return null;

		handle.Volume = Volume;
		handle.Parent = origin;
		handle.FollowParent = true;
		return handle;
	}

	private void StopLoopLocal()
	{
		loopHandle?.Stop( 0f );
		loopHandle = null;
	}

	// ───────── Idle call (networked one-shot) ─────────

	private void PlayIdle( float pitch )
	{
		if ( PlayForEveryone && Networking.IsActive )
			PlayIdleRpc( pitch );
		else
			PlayIdleLocal( pitch );
	}

	[Rpc.Broadcast] private void PlayIdleRpc( float pitch ) => PlayIdleLocal( pitch );

	private void PlayIdleLocal( float pitch )
	{
		if ( IdleRandomSound is null )
		{
			if ( DebugLog ) Log.Warning( "[HorseSfx] IdleRandomSound not assigned." );
			return;
		}

		var origin = SoundOrigin.IsValid() ? SoundOrigin : GameObject;
		var handle = Sound.Play( IdleRandomSound, origin.WorldPosition ); // 3D
		if ( handle is null ) return;

		handle.Volume = Volume;
		handle.Pitch = pitch;
		handle.Parent = origin;
		handle.FollowParent = true;
	}
}
