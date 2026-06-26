using Sandbox;
using System;

namespace Sandbox;

/// <summary>
/// Proximity SFX for the chariot's "chonk" (the rider/body on the wagon):
///   - Damage : Sound A when the player loses HP.
///   - HP     : Sound A when the player gains HP.
///   - Ram    : Sound A when the player rams another player (called by PlayerCollisions).
///
/// Put this on the chonk node. Damage/HP are detected by watching the player's
/// <see cref="PlayerDamageSystem.CurrentHP"/> on the OWNING client and broadcasting a
/// 3D sound that follows the chariot, so everyone hears it positioned at the player.
/// </summary>
public sealed class ChariotChonkSfxmodule : Component
{
	public enum Clip { Damage, Hp, Ram1, Ram2, Ram3 }

	[Property, Group( "Sounds" )] public SoundEvent DamageSound { get; set; } // took damage
	[Property, Group( "Sounds" )] public SoundEvent HpSound { get; set; }     // regained HP

	// One of these three is chosen at random on each ram.
	[Property, Group( "Ram Sounds" )] public SoundEvent RamSound1 { get; set; }
	[Property, Group( "Ram Sounds" )] public SoundEvent RamSound2 { get; set; }
	[Property, Group( "Ram Sounds" )] public SoundEvent RamSound3 { get; set; }

	[Property, Group( "Playback" ), Range( 0f, 2f )] public float Volume { get; set; } = 1f;

	/// <summary>World origin the 3D sounds emit from / follow. Defaults to this GameObject.</summary>
	[Property, Group( "Playback" )] public GameObject SoundOrigin { get; set; }

	/// <summary>True (default): broadcast so every player hears it at this chariot. False: owner only.</summary>
	[Property, Group( "Multiplayer" )] public bool PlayForEveryone { get; set; } = true;

	/// <summary>Optional explicit damage system. Auto-resolved from the player root if left null.</summary>
	[Property, Group( "Wiring" )] public PlayerDamageSystem Damage { get; set; }

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = false;

	/// <summary>Ignore HP changes smaller than this (float noise).</summary>
	private const float HpEpsilon = 0.01f;

	private bool hpInitialized;
	private float lastHp;

	protected override void OnStart()
	{
		if ( !SoundOrigin.IsValid() )
			SoundOrigin = GameObject;

		if ( !Damage.IsValid() )
			Damage = GameObject.Root?.Components.Get<PlayerDamageSystem>( FindMode.EverythingInSelfAndDescendants );
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;          // only the owner detects & broadcasts
		if ( !Damage.IsValid() ) return;

		float hp = Damage.CurrentHP;

		// Capture the starting value once so we don't fire a sound on the first frame.
		if ( !hpInitialized )
		{
			lastHp = hp;
			hpInitialized = true;
			return;
		}

		float delta = hp - lastHp;
		if ( delta <= -HpEpsilon )
			PlayProximity( Clip.Damage );
		else if ( delta >= HpEpsilon )
			PlayProximity( Clip.Hp );

		lastHp = hp;
	}

	/// <summary>
	/// Confirmed-ram hook. Called by PlayerCollisions on the attacker's owning peer, so
	/// the ram sound emits once from the player who landed the hit. Picks one of the
	/// three ram clips at random (chosen on the owner; the exact clip is broadcast so
	/// everyone hears the same one). Empty slots are skipped.
	/// </summary>
	public void PlayRam()
	{
		// Collect the assigned ram slots so an empty one never plays silence.
		Span<Clip> options = stackalloc Clip[3];
		int count = 0;
		if ( RamSound1 is not null ) options[count++] = Clip.Ram1;
		if ( RamSound2 is not null ) options[count++] = Clip.Ram2;
		if ( RamSound3 is not null ) options[count++] = Clip.Ram3;

		if ( count == 0 )
		{
			if ( DebugLog ) Log.Warning( "[ChonkSfx] PlayRam: no ram sounds assigned." );
			return;
		}

		PlayProximity( options[Random.Shared.Next( 0, count )] );
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
			if ( DebugLog ) Log.Warning( $"[ChonkSfx] {(Clip)clip} has no SoundEvent assigned." );
			return null;
		}

		var origin = SoundOrigin.IsValid() ? SoundOrigin : GameObject;
		var handle = Sound.Play( ev, origin.WorldPosition ); // 3D
		if ( handle is null ) return null;

		handle.Volume = Volume;
		handle.Parent = origin;       // follow the chariot
		handle.FollowParent = true;
		return handle;
	}

	private SoundEvent Resolve( Clip clip ) => clip switch
	{
		Clip.Damage => DamageSound,
		Clip.Hp => HpSound,
		Clip.Ram1 => RamSound1,
		Clip.Ram2 => RamSound2,
		Clip.Ram3 => RamSound3,
		_ => null,
	};
}
