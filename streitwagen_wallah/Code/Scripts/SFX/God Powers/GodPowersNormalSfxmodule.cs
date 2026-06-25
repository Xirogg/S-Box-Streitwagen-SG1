using Sandbox;
using System;
using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Per-player SFX module for the NORMAL god-power abilities. Put ONE on every player
/// prefab. Each player's own GodPower clone (which runs on that player's client) calls
/// the matching method here on activation; because the sounds live on the persistent
/// player, they keep playing even after the short-lived GodPower clone is destroyed.
///
/// Spatialisation per the design doc:
///   - Proximity = broadcast, 3D at the casting player and follows them, so everyone
///     hears it positioned at the user (Taranis, Ma'at, Dionysos).
///   - Local     = sent only to specific connections (the user and the affected
///     player), played 2D so those few hear it at full volume (Laverna).
///
/// Networking: the casting client drives the timing and broadcasts each clip. With no
/// active session (editor / single-player) everything just plays locally.
/// </summary>
public sealed class GodPowersNormalSfxmodule : Component
{
	/// <summary>Stable ids for the clips, sent over RPCs so no SoundEvent travels the wire.</summary>
	public enum NormalClip
	{
		TaranisCharge,      // Sound A — charge begins
		TaranisChargeLoop,  // Sound B — repeats while charging
		TaranisCharged,     // Sound C — fully charged / detonation
		MaatShield,         // Sound A — shield raised or destroyed
		DionysosGrapeA,     // Sound A
		DionysosGrapeB,     // Sound B
		LavernaStealA,      // Sound A
		LavernaStealB,      // Sound B
	}

	// ───────── Taranis — Blitz-Bombe charge (proximity) ─────────
	[Property, Group( "Taranis (proximity)" )] public SoundEvent TaranisChargeSound { get; set; }   // A
	[Property, Group( "Taranis (proximity)" )] public SoundEvent TaranisLoopSound { get; set; }      // B
	[Property, Group( "Taranis (proximity)" )] public SoundEvent TaranisChargedSound { get; set; }   // C

	/// <summary>Extra times Sound B repeats after its first play (2 ⇒ B plays 3× total).</summary>
	[Property, Group( "Taranis (proximity)" ), Range( 0, 10 )]
	public int TaranisLoopRepeats { get; set; } = 2;

	// ───────── Ma'at — Karma shield (proximity) ─────────
	[Property, Group( "Ma'at (proximity)" )] public SoundEvent MaatShieldSound { get; set; }         // A

	// ───────── Dionysos — Grape shot (proximity, random A/B) ─────────
	[Property, Group( "Dionysos (proximity)" )] public SoundEvent DionysosGrapeSoundA { get; set; }
	[Property, Group( "Dionysos (proximity)" )] public SoundEvent DionysosGrapeSoundB { get; set; }

	// ───────── Laverna — Item steal (local: user + victim) ─────────
	[Property, Group( "Laverna (local)" )] public SoundEvent LavernaStealSoundA { get; set; }
	[Property, Group( "Laverna (local)" )] public SoundEvent LavernaStealSoundB { get; set; }

	[Property, Group( "Playback" ), Range( 0f, 2f )]
	public float Volume { get; set; } = 1f;

	/// <summary>World origin proximity sounds emit from / follow. Defaults to this GameObject.</summary>
	[Property, Group( "Playback" )]
	public GameObject SoundOrigin { get; set; }

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = false;

	/// <summary>Safety cap (s) so a clip that never reports Finished can't stall a sequence.</summary>
	private const float MaxClipSeconds = 8f;

	protected override void OnStart()
	{
		if ( !SoundOrigin.IsValid() )
			SoundOrigin = GameObject;
	}

	// ════════════════════════════ Public API ════════════════════════════

	/// <summary>Dionysos normal: one random grape clip, proximity.</summary>
	public void PlayDionysosGrapes()
	{
		var clip = Random.Shared.Next( 0, 2 ) == 0 ? NormalClip.DionysosGrapeA : NormalClip.DionysosGrapeB;
		PlayProximity( clip );
	}

	/// <summary>Ma'at normal: shield raised or destroyed, proximity. Called by MaatKarmaShield.</summary>
	public void PlayMaatShield() => PlayProximity( NormalClip.MaatShield );

	/// <summary>Taranis normal: start the charge jingle (A, then B on a loop), proximity.</summary>
	public void StartTaranisCharge()
	{
		taranisActive = true;
		taranisStage = TaranisStage.Intro;
		taranisClipTime = 0f;
		taranisHandle = PlayProximity( NormalClip.TaranisCharge ); // Sound A
		if ( DebugLog ) Log.Info( "[NormalSfx] Taranis charge started." );
	}

	/// <summary>Taranis normal: fully charged / detonation — stop the loop and play C, proximity.</summary>
	public void TaranisCharged()
	{
		taranisActive = false;
		taranisStage = TaranisStage.Done;
		taranisHandle = null;
		PlayProximity( NormalClip.TaranisCharged ); // Sound C
		if ( DebugLog ) Log.Info( "[NormalSfx] Taranis charged → Sound C." );
	}

	/// <summary>
	/// Laverna normal: Sound A then Sound B, heard ONLY by the caster and the victim.
	/// Pass the victim's player root so we can resolve their connection.
	/// </summary>
	public void PlayLavernaSteal( GameObject victimRoot )
	{
		lavernaTargets = BuildLocalTargets( victimRoot );
		lavernaActive = true;
		lavernaStage = 0;
		lavernaClipTime = 0f;
		lavernaHandle = PlayLocal( NormalClip.LavernaStealA, lavernaTargets ); // Sound A
		if ( DebugLog ) Log.Info( $"[NormalSfx] Laverna steal sound → {lavernaTargets?.Count ?? 0} listener(s)." );
	}

	// ════════════════════════════ Sequencing ════════════════════════════
	// Only the casting client (owner) drives timing; clips are broadcast to everyone.

	private enum TaranisStage { Intro, Loop, Done }
	private bool taranisActive;
	private TaranisStage taranisStage;
	private int taranisLoopLeft;
	private SoundHandle taranisHandle;
	private float taranisClipTime;

	private bool lavernaActive;
	private int lavernaStage; // 0 = A playing, 1 = B playing
	private SoundHandle lavernaHandle;
	private float lavernaClipTime;
	private List<Connection> lavernaTargets;

	protected override void OnUpdate()
	{
		if ( Network.IsProxy ) return; // only the owner advances its own sequences
		TickTaranis();
		TickLaverna();
	}

	private void TickTaranis()
	{
		if ( !taranisActive || taranisStage == TaranisStage.Done ) return;

		taranisClipTime += Time.Delta;
		bool clipDone = taranisHandle is null || taranisHandle.Finished || taranisClipTime >= MaxClipSeconds;
		if ( !clipDone ) return;

		if ( taranisStage == TaranisStage.Intro )
		{
			// Sound A finished → queue the B loop.
			taranisStage = TaranisStage.Loop;
			taranisLoopLeft = 1 + Math.Max( 0, TaranisLoopRepeats );
		}

		if ( taranisLoopLeft > 0 )
		{
			taranisLoopLeft--;
			taranisClipTime = 0f;
			taranisHandle = PlayProximity( NormalClip.TaranisChargeLoop ); // Sound B
			return;
		}

		// Loop exhausted before "charged" arrived — sit idle until TaranisCharged() plays C.
		taranisStage = TaranisStage.Done;
		taranisHandle = null;
	}

	private void TickLaverna()
	{
		if ( !lavernaActive ) return;

		lavernaClipTime += Time.Delta;
		bool clipDone = lavernaHandle is null || lavernaHandle.Finished || lavernaClipTime >= MaxClipSeconds;
		if ( !clipDone ) return;

		if ( lavernaStage == 0 )
		{
			lavernaStage = 1;
			lavernaClipTime = 0f;
			lavernaHandle = PlayLocal( NormalClip.LavernaStealB, lavernaTargets ); // Sound B
			return;
		}

		lavernaActive = false;
		lavernaHandle = null;
		lavernaTargets = null;
	}

	// ════════════════════════════ Playback ════════════════════════════

	private SoundHandle lastHandle; // owner-local handle of the most recent clip (for polling)

	/// <summary>Proximity: 3D at the player, broadcast so everyone hears it positioned.</summary>
	private SoundHandle PlayProximity( NormalClip clip )
	{
		if ( Networking.IsActive )
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
		var ev = Resolve( (NormalClip)clip );
		if ( ev is null )
		{
			if ( DebugLog ) Log.Warning( $"[NormalSfx] {(NormalClip)clip} has no SoundEvent assigned." );
			return null;
		}

		var origin = SoundOrigin.IsValid() ? SoundOrigin : GameObject;
		var handle = Sound.Play( ev, origin.WorldPosition ); // 3D
		if ( handle is null ) return null;

		handle.Volume = Volume;
		// Pin to the player so the sound travels with the chariot instead of staying put.
		handle.Parent = origin;
		handle.FollowParent = true;
		return handle;
	}

	/// <summary>Local: 2D, sent only to the given connections (caster + affected player).</summary>
	private SoundHandle PlayLocal( NormalClip clip, List<Connection> targets )
	{
		if ( Networking.IsActive && targets is not null && targets.Count > 0 )
		{
			using ( Rpc.FilterInclude( targets ) )
				PlayLocalRpc( (int)clip );
			return lastHandle;
		}
		return PlayLocalReal( (int)clip );
	}

	[Rpc.Broadcast]
	private void PlayLocalRpc( int clip ) => lastHandle = PlayLocalReal( clip );

	private SoundHandle PlayLocalReal( int clip )
	{
		var ev = Resolve( (NormalClip)clip );
		if ( ev is null )
		{
			if ( DebugLog ) Log.Warning( $"[NormalSfx] {(NormalClip)clip} has no SoundEvent assigned." );
			return null;
		}

		var handle = Sound.Play( ev ); // 2D, full volume for the few listeners
		if ( handle is not null )
			handle.Volume = Volume;
		return handle;
	}

	/// <summary>The caster's own connection plus the affected player's, de-duplicated.</summary>
	private static List<Connection> BuildLocalTargets( GameObject victimRoot )
	{
		var list = new List<Connection>();

		var me = Connection.Local;
		if ( me is not null ) list.Add( me );

		var victim = victimRoot.IsValid() ? victimRoot.Network.Owner : null;
		if ( victim is not null && !list.Contains( victim ) ) list.Add( victim );

		return list;
	}

	private SoundEvent Resolve( NormalClip clip ) => clip switch
	{
		NormalClip.TaranisCharge => TaranisChargeSound,
		NormalClip.TaranisChargeLoop => TaranisLoopSound,
		NormalClip.TaranisCharged => TaranisChargedSound,
		NormalClip.MaatShield => MaatShieldSound,
		NormalClip.DionysosGrapeA => DionysosGrapeSoundA,
		NormalClip.DionysosGrapeB => DionysosGrapeSoundB,
		NormalClip.LavernaStealA => LavernaStealSoundA,
		NormalClip.LavernaStealB => LavernaStealSoundB,
		_ => null,
	};
}
