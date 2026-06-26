using Sandbox;
using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Proximity SFX for the chariot wagon body:
///   - Airtime    : Sound A the moment the chariot leaves the ground.
///   - Sudden Stop: Sound A when it loses more than <see cref="SpeedDropThreshold"/>
///                  speed within <see cref="DropWindow"/> seconds (e.g. a hard crash).
///
/// Put this on the wagon node. Detection runs on the OWNING client (ground raycast +
/// a short speed history on the wagon Rigidbody) and broadcasts a 3D sound that follows
/// the chariot, so everyone hears it positioned at the player.
/// </summary>
public sealed class ChariotWagonSfxmodule : Component
{
	public enum Clip { Airtime, SuddenStop }

	[Property, Group( "Sounds" )] public SoundEvent AirtimeSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent SuddenStopSound { get; set; }

	/// <summary>Down-ray length used to decide "grounded". Beyond this = airborne.</summary>
	[Property, Group( "Airtime" )] public float GroundProbeDistance { get; set; } = 60f;

	/// <summary>Speed that must be lost within <see cref="DropWindow"/> to count as a sudden stop.</summary>
	[Property, Group( "Sudden Stop" )] public float SpeedDropThreshold { get; set; } = 1500f;

	/// <summary>Sliding window (seconds) the speed drop is measured over.</summary>
	[Property, Group( "Sudden Stop" )] public float DropWindow { get; set; } = 0.5f;

	/// <summary>Minimum gap between sudden-stop sounds, so one crash fires it once.</summary>
	[Property, Group( "Sudden Stop" )] public float StopCooldown { get; set; } = 1f;

	[Property, Group( "Playback" ), Range( 0f, 2f )] public float Volume { get; set; } = 1f;

	/// <summary>World origin the 3D sounds emit from / follow. Defaults to this GameObject.</summary>
	[Property, Group( "Playback" )] public GameObject SoundOrigin { get; set; }

	[Property, Group( "Multiplayer" )] public bool PlayForEveryone { get; set; } = true;

	/// <summary>The wagon Rigidbody. Auto-resolved from the player's ChariotPhysics if left null.</summary>
	[Property, Group( "Wiring" )] public Rigidbody Body { get; set; }

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = false;

	private bool groundedInit;
	private bool wasGrounded;

	private readonly List<(float time, float speed)> speedSamples = new();
	private float stopCooldownUntil;

	protected override void OnStart()
	{
		if ( !SoundOrigin.IsValid() )
			SoundOrigin = GameObject;

		if ( !Body.IsValid() )
		{
			var chariot = GameObject.Root?.Components.Get<ChariotPhysics>( FindMode.EverythingInSelfAndDescendants );
			Body = chariot?.Body ?? Components.Get<Rigidbody>( FindMode.EverythingInSelfAndAncestors );
		}
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;       // only the owner detects & broadcasts
		if ( !Body.IsValid() ) return;

		TickAirtime();
		TickSuddenStop();
	}

	// ───────── Airtime ─────────

	private void TickAirtime()
	{
		bool grounded = IsGrounded();

		if ( !groundedInit )
		{
			// Seed the state so we don't fire on the very first frame at spawn.
			wasGrounded = grounded;
			groundedInit = true;
			return;
		}

		if ( wasGrounded && !grounded )
			PlayProximity( Clip.Airtime ); // just left the ground

		wasGrounded = grounded;
	}

	private bool IsGrounded()
	{
		Vector3 from = Body.WorldPosition;
		Vector3 to = from + Vector3.Down * GroundProbeDistance;
		var tr = Scene.Trace
			.Ray( from, to )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();
		return tr.Hit;
	}

	// ───────── Sudden stop ─────────

	private void TickSuddenStop()
	{
		float now = Time.Now;
		float speed = Body.Velocity.Length;

		speedSamples.Add( (now, speed) );

		// Drop samples older than the window.
		float cutoff = now - DropWindow;
		int stale = 0;
		while ( stale < speedSamples.Count && speedSamples[stale].time < cutoff )
			stale++;
		if ( stale > 0 )
			speedSamples.RemoveRange( 0, stale );

		if ( now < stopCooldownUntil ) return;

		// Compare the current speed to the fastest we were going in the window.
		float peak = 0f;
		foreach ( var s in speedSamples )
			if ( s.speed > peak ) peak = s.speed;

		if ( peak - speed >= SpeedDropThreshold )
		{
			PlayProximity( Clip.SuddenStop );
			stopCooldownUntil = now + StopCooldown;
			speedSamples.Clear(); // re-arm only after speed builds up again
		}
	}

	// ───────── proximity playback ─────────

	private SoundHandle lastHandle;

	private SoundHandle PlayProximity( Clip clip )
	{
		if ( PlayForEveryone && Networking.IsActive )
		{
			PlayProximityRpc( (int)clip );
			return lastHandle;
		}
		return PlayProximityLocal( (int)clip );
	}

	[Rpc.Broadcast]
	private void PlayProximityRpc( int clip ) => lastHandle = PlayProximityLocal( clip );

	private SoundHandle PlayProximityLocal( int clip )
	{
		var ev = Resolve( (Clip)clip );
		if ( ev is null )
		{
			if ( DebugLog ) Log.Warning( $"[WagonSfx] {(Clip)clip} has no SoundEvent assigned." );
			return null;
		}

		var origin = SoundOrigin.IsValid() ? SoundOrigin : GameObject;
		var handle = Sound.Play( ev, origin.WorldPosition ); // 3D
		if ( handle is null ) return null;

		handle.Volume = Volume;
		handle.Parent = origin;
		handle.FollowParent = true;
		return handle;
	}

	private SoundEvent Resolve( Clip clip ) => clip switch
	{
		Clip.Airtime => AirtimeSound,
		Clip.SuddenStop => SuddenStopSound,
		_ => null,
	};
}
