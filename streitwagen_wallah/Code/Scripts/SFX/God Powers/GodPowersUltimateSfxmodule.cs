using Sandbox;

namespace Sandbox;

/// <summary>
/// Scene-wide SFX module for every god power's ULTIMATE.
///
/// All ultimate clips play WORLDWIDE — every player hears them equally, like
/// background music. We force each handle to 2D (<c>SpacialBlend = 0</c>,
/// <c>DistanceAttenuation = false</c>) so it is truly non-directional even if the
/// underlying .sound asset is authored as a 3D event (otherwise a position-less 3D
/// clip is anchored at world origin and it sounds like it comes from a direction).
///
/// Per-god timing (the rest fire all clips at once):
///   - Ma'at      : Sound A + Sound B + Voice, all together.
///   - Dionysos   : Sound A + Voice now, then Sound B once Sound A finishes.
///   - Taranis    : Sound A + Voice together.
///   - Laverna    : Sound A + Voice together.
/// Overlapping ultimates just stack — every call fires its own independent clips.
///
/// Setup:
///   - Put exactly ONE of these on a networked scene object (same as your item
///     boxes — it must be network-enabled so the broadcast reaches every client).
///   - Drag the .sound assets into the per-god slots below.
///   - Each GodPower's Ultimate calls the matching Play*Ultimate() through the
///     static <see cref="Instance"/>.
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
		MaatSound, MaatSoundB, MaatVoice,
		DionysosSound, DionysosSoundB, DionysosVoice,
		LavernaSound, LavernaVoice,
	}

	[Property, Group( "Taranis" )] public SoundEvent TaranisSound { get; set; }
	[Property, Group( "Taranis" )] public SoundEvent TaranisVoice { get; set; }

	[Property, Group( "Ma'at" )] public SoundEvent MaatSound { get; set; }        // A
	[Property, Group( "Ma'at" )] public SoundEvent MaatSoundB { get; set; }       // B
	[Property, Group( "Ma'at" )] public SoundEvent MaatVoice { get; set; }        // V

	[Property, Group( "Dionysos" )] public SoundEvent DionysosSound { get; set; }   // A
	[Property, Group( "Dionysos" )] public SoundEvent DionysosSoundB { get; set; }  // B (after A)
	[Property, Group( "Dionysos" )] public SoundEvent DionysosVoice { get; set; }   // V

	[Property, Group( "Laverna" )] public SoundEvent LavernaSound { get; set; }
	[Property, Group( "Laverna" )] public SoundEvent LavernaVoice { get; set; }

	/// <summary>Constant playback volume for every ultimate clip.</summary>
	[Property, Group( "Playback" ), Range( 0f, 2f )]
	public float Volume { get; set; } = 1f;

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = false;

	/// <summary>Safety cap (s) so Dionysos' Sound B still fires if Sound A never reports Finished.</summary>
	private const float MaxClipSeconds = 10f;

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

	public void PlayTaranisUltimate()
	{
		PlayWorldwide( UltClip.TaranisSound );
		PlayWorldwide( UltClip.TaranisVoice );
	}

	/// <summary>Ma'at: Sound A + Sound B + Voice, all at once, worldwide.</summary>
	public void PlayMaatUltimate()
	{
		PlayWorldwide( UltClip.MaatSound );
		PlayWorldwide( UltClip.MaatSoundB );
		PlayWorldwide( UltClip.MaatVoice );
	}

	/// <summary>Dionysos: Sound A + Voice now; Sound B once Sound A finishes. All worldwide.</summary>
	public void PlayDionysosUltimate()
	{
		PlayWorldwide( UltClip.DionysosVoice );
		// Track Sound A's handle locally so we can play Sound B when it ends.
		dionysosHandle = PlayWorldwideTracked( UltClip.DionysosSound );
		dionysosActive = true;
		dionysosTime = 0f;
		if ( DebugLog ) Log.Info( "[UltSfx] Dionysos ult: A + V now, B after A." );
	}

	public void PlayLavernaUltimate()
	{
		PlayWorldwide( UltClip.LavernaSound );
		PlayWorldwide( UltClip.LavernaVoice );
	}

	// ───────────── Dionysos A → B sequence (driven by the casting client only) ─────────────

	private bool dionysosActive;
	private SoundHandle dionysosHandle;
	private float dionysosTime;

	protected override void OnUpdate()
	{
		if ( !dionysosActive ) return; // only true on the client that cast Dionysos' ult

		dionysosTime += Time.Delta;
		bool soundADone = dionysosHandle is null || dionysosHandle.Finished || dionysosTime >= MaxClipSeconds;
		if ( !soundADone ) return;

		dionysosActive = false;
		dionysosHandle = null;
		PlayWorldwide( UltClip.DionysosSoundB ); // Sound B, after A
		if ( DebugLog ) Log.Info( "[UltSfx] Dionysos Sound A finished → Sound B." );
	}

	// ───────────── Playback ─────────────

	/// <summary>Fire-and-forget worldwide clip: plays on every client, no handle kept.</summary>
	private void PlayWorldwide( UltClip clip )
	{
		if ( Networking.IsActive )
			PlayWorldwideRpc( (int)clip );
		else
			PlayLocal( (int)clip );
	}

	/// <summary>
	/// Worldwide clip that ALSO returns this client's local handle, so the caster can
	/// poll Finished. Others hear it via broadcast; we play it locally once ourselves
	/// (excluded from the broadcast) to avoid a double play on the caster.
	/// </summary>
	private SoundHandle PlayWorldwideTracked( UltClip clip )
	{
		if ( Networking.IsActive )
		{
			using ( Rpc.FilterExclude( Connection.Local ) )
				PlayWorldwideRpc( (int)clip );
		}
		return PlayLocal( (int)clip );
	}

	[Rpc.Broadcast]
	private void PlayWorldwideRpc( int clip ) => PlayLocal( clip );

	private SoundHandle PlayLocal( int clip )
	{
		var ev = Resolve( (UltClip)clip );
		if ( ev is null )
		{
			if ( DebugLog ) Log.Warning( $"[UltSfx] {(UltClip)clip} has no SoundEvent assigned." );
			return null;
		}

		var handle = Sound.Play( ev );
		if ( handle is null ) return null;

		handle.Volume = Volume;
		// Force truly worldwide: no 3D panning, no distance falloff, even if the .sound
		// asset itself is authored as a 3D event.
		handle.SpacialBlend = 0f;
		handle.DistanceAttenuation = false;
		return handle;
	}

	private SoundEvent Resolve( UltClip clip ) => clip switch
	{
		UltClip.TaranisSound => TaranisSound,
		UltClip.TaranisVoice => TaranisVoice,
		UltClip.MaatSound => MaatSound,
		UltClip.MaatSoundB => MaatSoundB,
		UltClip.MaatVoice => MaatVoice,
		UltClip.DionysosSound => DionysosSound,
		UltClip.DionysosSoundB => DionysosSoundB,
		UltClip.DionysosVoice => DionysosVoice,
		UltClip.LavernaSound => LavernaSound,
		UltClip.LavernaVoice => LavernaVoice,
		_ => null,
	};
}
