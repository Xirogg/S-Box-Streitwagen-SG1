using Sandbox;
using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Proximity SFX for the chariot wagon body:
///   - Airtime    : Sound A starts the moment the chariot leaves the ground and is
///                  CUT OFF again as soon as it lands.
///   - Sudden Stop: Sound A starts when it loses more than <see cref="SpeedDropThreshold"/>
///                  speed within <see cref="DropWindow"/> seconds, and is CUT OFF once the
///                  chariot is moving again (speed back above <see cref="ResumeSpeed"/>).
///
/// Both sounds are long clips, so we keep their handles and stop them when the condition
/// ends instead of letting the whole clip play out. Detection runs on the OWNING client
/// and the start/stop is broadcast so everyone hears it positioned at the player.
/// </summary>
public sealed class ChariotWagonSfxmodule : Component
{
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

	/// <summary>Speed the chariot must regain for the sudden-stop sound to cut off ("moving again").</summary>
	[Property, Group( "Sudden Stop" )] public float ResumeSpeed { get; set; } = 400f;

	[Property, Group( "Playback" ), Range( 0f, 2f )] public float Volume { get; set; } = 1f;

	/// <summary>World origin the 3D sounds emit from / follow. Defaults to this GameObject.</summary>
	[Property, Group( "Playback" )] public GameObject SoundOrigin { get; set; }

	[Property, Group( "Multiplayer" )] public bool PlayForEveryone { get; set; } = true;

	/// <summary>The wagon Rigidbody. Auto-resolved from the player's ChariotPhysics if left null.</summary>
	[Property, Group( "Wiring" )] public Rigidbody Body { get; set; }

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = false;

	/// <summary>Short fade (seconds) when a sound is cut off, to avoid a click.</summary>
	private const float StopFade = 0.1f;

	// Airtime state
	private bool groundedInit;
	private bool wasGrounded;
	private SoundHandle airtimeHandle;

	// Sudden-stop state
	private readonly List<(float time, float speed)> speedSamples = new();
	private float stopCooldownUntil;
	private SoundHandle stopHandle;
	private bool stopSoundActive;

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

	protected override void OnDisabled() => StopAll();
	protected override void OnDestroy() => StopAll();

	private void StopAll()
	{
		StopHandleLocal( ref airtimeHandle );
		StopHandleLocal( ref stopHandle );
		stopSoundActive = false;
	}

	// ───────── Airtime: play while airborne, cut on landing ─────────

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
			StartAirtime();              // just left the ground
		else if ( !wasGrounded && grounded )
			StopAirtime();               // back on the ground → cut it

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

	// ───────── Sudden stop: play on the drop, cut once moving again ─────────

	private void TickSuddenStop()
	{
		float now = Time.Now;
		float speed = Body.Velocity.Length;

		// Cut the crash sound once we've picked up speed again.
		if ( stopSoundActive && speed >= ResumeSpeed )
		{
			StopSuddenStop();
			stopSoundActive = false;
		}

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
			StartSuddenStop();
			stopSoundActive = true;
			stopCooldownUntil = now + StopCooldown;
			speedSamples.Clear(); // re-arm only after speed builds up again
		}
	}

	// ───────── networked start/stop ─────────

	private void StartAirtime()
	{
		if ( PlayForEveryone && Networking.IsActive ) StartAirtimeRpc();
		else airtimeHandle = PlayLocal( AirtimeSound );
	}

	private void StopAirtime()
	{
		if ( PlayForEveryone && Networking.IsActive ) StopAirtimeRpc();
		else StopHandleLocal( ref airtimeHandle );
	}

	private void StartSuddenStop()
	{
		if ( PlayForEveryone && Networking.IsActive ) StartSuddenStopRpc();
		else stopHandle = PlayLocal( SuddenStopSound );
	}

	private void StopSuddenStop()
	{
		if ( PlayForEveryone && Networking.IsActive ) StopSuddenStopRpc();
		else StopHandleLocal( ref stopHandle );
	}

	[Rpc.Broadcast] private void StartAirtimeRpc() => airtimeHandle = PlayLocal( AirtimeSound );
	[Rpc.Broadcast] private void StopAirtimeRpc() => StopHandleLocal( ref airtimeHandle );
	[Rpc.Broadcast] private void StartSuddenStopRpc() => stopHandle = PlayLocal( SuddenStopSound );
	[Rpc.Broadcast] private void StopSuddenStopRpc() => StopHandleLocal( ref stopHandle );

	// ───────── playback helpers ─────────

	private SoundHandle PlayLocal( SoundEvent ev )
	{
		if ( ev is null )
		{
			if ( DebugLog ) Log.Warning( "[WagonSfx] A SoundEvent is not assigned." );
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

	private void StopHandleLocal( ref SoundHandle handle )
	{
		handle?.Stop( StopFade );
		handle = null;
	}
}
