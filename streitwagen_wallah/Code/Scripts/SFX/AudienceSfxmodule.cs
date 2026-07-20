using Sandbox;
using System;

namespace Sandbox;

/// <summary>
/// Ambient crowd cheering for a tribune. Plays a cheer clip, waits for it to actually
/// finish, pauses for a randomized gap (<see cref="MinGap"/>..<see cref="MaxGap"/>), then
/// cheers again — forever, positioned at (and following) this GameObject.
///
/// Put this on the Audience_SFX prefab and scatter one per tribune.
///
/// This is deliberately NOT networked. Ambient scenery audio has no state worth syncing:
/// every client runs this component itself and hears its own local copy. That's why there
/// is no <c>IsProxy</c> check and no Rpc — unlike the chariot modules, where the OWNER has
/// to tell everyone else about an event they can't observe. Broadcasting cheers would just
/// burn bandwidth and drift out of sync anyway.
///
/// The loop is driven in code off <c>SoundHandle.Finished</c> rather than a loop flag,
/// because a flag would give a seamless loop with no gap — and the gap is the point.
/// </summary>
public sealed class AudienceSfxmodule : Component
{
	[Property, Group( "Sounds" )] public SoundEvent CheerSound { get; set; }

	/// <summary>Shortest pause between the clip ending and the next cheer starting.</summary>
	[Property, Group( "Timing" )] public float MinGap { get; set; } = 1f;

	/// <summary>Longest pause between the clip ending and the next cheer starting.</summary>
	[Property, Group( "Timing" )] public float MaxGap { get; set; } = 2f;

	/// <summary>
	/// Random delay before this tribune's FIRST cheer. Without this, every tribune in the
	/// scene starts on the same frame and stays phase-locked, so the whole stadium cheers
	/// in unison like one giant speaker. Keep this at least as long as the clip.
	/// </summary>
	[Property, Group( "Timing" )] public float StartStagger { get; set; } = 6f;

	[Property, Group( "Playback" ), Range( 0f, 2f )] public float Volume { get; set; } = 1f;

	/// <summary>Per-tribune pitch variation, rolled once per cheer so neighbouring
	/// tribunes don't sound like the same clip copy-pasted.</summary>
	[Property, Group( "Playback" ), Range( 0.1f, 2f )] public float PitchMin { get; set; } = 0.95f;
	[Property, Group( "Playback" ), Range( 0.1f, 2f )] public float PitchMax { get; set; } = 1.05f;

	/// <summary>World origin the 3D sound emits from / follows. Defaults to this GameObject.</summary>
	[Property, Group( "Playback" )] public GameObject SoundOrigin { get; set; }

	/// <summary>
	/// Don't even start a voice unless the camera is within this range. With many tribunes
	/// this is the difference between a handful of active voices and dozens. Set it a little
	/// wider than the falloff Distance on the .sound asset so nothing cuts in audibly.
	/// </summary>
	[Property, Group( "Culling" )] public bool CullByDistance { get; set; } = true;
	[Property, Group( "Culling" )] public float HearingRange { get; set; } = 4000f;

	/// <summary>Fade applied when a cheer is cut short by distance culling, so it doesn't pop.</summary>
	[Property, Group( "Culling" )] public float CullFadeTime { get; set; } = 0.4f;

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = false;

	private SoundHandle handle;
	private float nextPlayAt;

	protected override void OnStart()
	{
		if ( !SoundOrigin.IsValid() )
			SoundOrigin = GameObject;

		ScheduleFirstCheer();
	}

	protected override void OnUpdate()
	{
		if ( CheerSound is null ) return;

		if ( CullByDistance && !ListenerInRange() )
		{
			// Out of earshot — drop the voice and re-stagger, so walking back into range
			// doesn't line this tribune up with every other one that did the same.
			if ( handle is not null )
			{
				StopCheer( CullFadeTime );
				ScheduleFirstCheer();
			}
			return;
		}

		if ( handle is not null )
		{
			if ( !handle.Finished ) return; // still cheering

			// Clip just ended — start the pause.
			handle = null;
			nextPlayAt = Time.Now + Random.Shared.Float( MinGap, MaxGap );
			return;
		}

		if ( Time.Now >= nextPlayAt )
			PlayCheer();
	}

	protected override void OnDisabled() => StopCheer( 0f );

	protected override void OnDestroy() => StopCheer( 0f );

	private void ScheduleFirstCheer() => nextPlayAt = Time.Now + Random.Shared.Float( 0f, StartStagger );

	private bool ListenerInRange()
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return true; // no listener yet — don't silence the scene

		var origin = SoundOrigin.IsValid() ? SoundOrigin : GameObject;
		return origin.WorldPosition.Distance( camera.WorldPosition ) <= HearingRange;
	}

	private void PlayCheer()
	{
		var origin = SoundOrigin.IsValid() ? SoundOrigin : GameObject;

		handle = Sound.Play( CheerSound, origin.WorldPosition ); // 3D
		if ( handle is null )
		{
			if ( DebugLog ) Log.Warning( "[AudienceSfx] Sound.Play returned null." );
			// Don't spin retrying every frame if something's wrong with the asset.
			nextPlayAt = Time.Now + Random.Shared.Float( MinGap, MaxGap );
			return;
		}

		handle.Volume = Volume;
		handle.Pitch = Random.Shared.Float( PitchMin, PitchMax );
		handle.Parent = origin;
		handle.FollowParent = true;

		if ( DebugLog ) Log.Info( $"[AudienceSfx] Cheer at {origin.WorldPosition}" );
	}

	private void StopCheer( float fadeTime )
	{
		handle?.Stop( fadeTime );
		handle = null;
	}
}
