using Sandbox;

namespace Sandbox;

/// <summary>
/// Scene-wide SFX module for every god power's ULTIMATE.
///
/// All ultimates behave identically: on activation they play a "Sound A" plus a
/// voice-acting line ("Sound V") at the SAME time, WORLDWIDE — every player hears
/// them equally, like background music (2D, no distance falloff). Overlapping
/// ultimates just stack, because every call fires its own independent pair of clips.
///
/// Setup:
///   - Put exactly ONE of these on a networked scene object (same as your item
///     boxes — it must be network-enabled so the broadcast reaches every client).
///   - Drag the eight .sound assets into the per-god slots below.
///   - Each GodPower's Ultimate calls the matching Play*Ultimate() through the
///     static <see cref="Instance"/>; the GodPower clone itself can be destroyed
///     immediately afterwards because the sound lives here, not on the power.
///
/// Networking: the casting client invokes a [Rpc.Broadcast], so the clips play on
/// every client regardless of who owns this object. With no active session (editor /
/// single-player) the calls just play locally.
/// </summary>
public sealed class GodPowersUltimateSfxmodule : Component
{
	/// <summary>Stable ids for the clips, sent over the RPC so no SoundEvent travels the wire.</summary>
	public enum UltClip
	{
		TaranisSound, TaranisVoice,
		MaatSound, MaatVoice,
		DionysosSound, DionysosVoice,
		LavernaSound, LavernaVoice,
	}

	[Property, Group( "Taranis" )] public SoundEvent TaranisSound { get; set; }
	[Property, Group( "Taranis" )] public SoundEvent TaranisVoice { get; set; }

	[Property, Group( "Ma'at" )] public SoundEvent MaatSound { get; set; }
	[Property, Group( "Ma'at" )] public SoundEvent MaatVoice { get; set; }

	[Property, Group( "Dionysos" )] public SoundEvent DionysosSound { get; set; }
	[Property, Group( "Dionysos" )] public SoundEvent DionysosVoice { get; set; }

	[Property, Group( "Laverna" )] public SoundEvent LavernaSound { get; set; }
	[Property, Group( "Laverna" )] public SoundEvent LavernaVoice { get; set; }

	/// <summary>Constant playback volume for every ultimate clip.</summary>
	[Property, Group( "Playback" ), Range( 0f, 2f )]
	public float Volume { get; set; } = 1f;

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = false;

	/// <summary>The scene's ultimate SFX module. God powers fire ult sounds through this.</summary>
	public static GodPowersUltimateSfxmodule Instance { get; private set; }

	protected override void OnEnabled()
	{
		if ( Instance.IsValid() && Instance != this )
			Log.Warning( "[UltSfx] A second GodPowersUltimateSfxmodule was enabled — the last one wins." );
		Instance = this;
	}

	protected override void OnDisabled()
	{
		if ( Instance == this ) Instance = null;
	}

	// ───────────── Public API — one per god, called on Ultimate activation ─────────────

	public void PlayTaranisUltimate() => PlayPair( UltClip.TaranisSound, UltClip.TaranisVoice );
	public void PlayMaatUltimate() => PlayPair( UltClip.MaatSound, UltClip.MaatVoice );
	public void PlayDionysosUltimate() => PlayPair( UltClip.DionysosSound, UltClip.DionysosVoice );
	public void PlayLavernaUltimate() => PlayPair( UltClip.LavernaSound, UltClip.LavernaVoice );

	/// <summary>Fire the sound + voice clip together, worldwide.</summary>
	private void PlayPair( UltClip sound, UltClip voice )
	{
		if ( DebugLog ) Log.Info( $"[UltSfx] {sound} + {voice} (worldwide)." );

		if ( Networking.IsActive )
		{
			PlayWorldwideRpc( (int)sound );
			PlayWorldwideRpc( (int)voice );
		}
		else
		{
			PlayLocal( (int)sound );
			PlayLocal( (int)voice );
		}
	}

	[Rpc.Broadcast]
	private void PlayWorldwideRpc( int clip ) => PlayLocal( clip );

	private void PlayLocal( int clip )
	{
		var ev = Resolve( (UltClip)clip );
		if ( ev is null )
		{
			if ( DebugLog ) Log.Warning( $"[UltSfx] {(UltClip)clip} has no SoundEvent assigned." );
			return;
		}

		var handle = Sound.Play( ev ); // 2D = worldwide, full volume for everyone
		if ( handle is not null )
			handle.Volume = Volume;
	}

	private SoundEvent Resolve( UltClip clip ) => clip switch
	{
		UltClip.TaranisSound => TaranisSound,
		UltClip.TaranisVoice => TaranisVoice,
		UltClip.MaatSound => MaatSound,
		UltClip.MaatVoice => MaatVoice,
		UltClip.DionysosSound => DionysosSound,
		UltClip.DionysosVoice => DionysosVoice,
		UltClip.LavernaSound => LavernaSound,
		UltClip.LavernaVoice => LavernaVoice,
		_ => null,
	};
}
