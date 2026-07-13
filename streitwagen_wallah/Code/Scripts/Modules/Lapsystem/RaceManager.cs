using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace LapSystem;

public sealed class RaceManager : Component
{
	public static RaceManager Instance { get; private set; }

	[Property] public int MaxLaps { get; set; } = 3;
	[Property] public SceneFile LobbyScene { get; set; }

	// Start-countdown SFX. Each is played LOCALLY on every machine the moment the
	// HUD number it matches first appears (see CountdownPhase). Because every client
	// already has the synced StartCountdownTimeLeft, each one watches it and plays
	// its own 2D sound — no RPCs, so everyone hears the countdown for themselves.
	// Assign these in the inspector to the clips in Assets/Music/SFX/InGame Race.
	[Property, Group( "Countdown Sounds" )] public SoundEvent Countdown3Sound { get; set; } // "3"
	[Property, Group( "Countdown Sounds" )] public SoundEvent Countdown2Sound { get; set; } // "2"
	[Property, Group( "Countdown Sounds" )] public SoundEvent Countdown1Sound { get; set; } // "1"
	[Property, Group( "Countdown Sounds" )] public SoundEvent GoSound { get; set; }         // "GO!"
	[Property, Group( "Countdown Sounds" ), Range( 0f, 2f )] public float CountdownVolume { get; set; } = 1f;

	public event Action OnRaceStarted;
	public event Action<PlayerLapTracker> OnPlayerFinished;

	[Sync] public bool ReturnCountdownActive { get; set; }
	[Sync] public float ReturnCountdownTimeLeft { get; set; }

	[Sync] public bool StartCountdownActive { get; set; }
	[Sync] public float StartCountdownTimeLeft { get; set; }

	// Overall race timer. Starts the moment the countdown hits GO (the same instant the
	// players are allowed to move, see TestControlls) and runs host-authoritatively.
	// RaceTime is the live elapsed time; both are synced so any HUD can show them.
	[Sync] public bool RaceTimerRunning { get; set; }
	[Sync] public float RaceTime { get; set; }

	private readonly List<SectorCheckpoint> sectors = new();
	public IReadOnlyList<SectorCheckpoint> Sectors => sectors;

	private float returnCountdownStartTime;
	private const float ReturnCountdownDuration = 6f;

	private float startCountdownStartTime;
	private const float StartCountdownDuration = 5f;
	private const float GoDisplayDuration = 1f;

	// Host-local timer bookkeeping. raceStartTime is the exact host-clock moment of GO
	// (= startCountdownStartTime + StartCountdownDuration), so every finish time is
	// measured against one consistent origin on the authoritative machine.
	private float raceStartTime;
	private bool raceTimerStarted;

	// Last countdown step this client already played a sound for. Per-machine (not
	// synced) so each player triggers their own local SFX exactly once per step.
	private int lastCountdownPhase = -1;

	protected override void OnAwake()
	{
		Instance = this;

		if ( Networking.IsHost )
			GameObject.NetworkSpawn();

		PublicityCurrencyManager.EnsureExists( Scene );
		RaceRecordManager.EnsureExists( Scene );
	}

	protected override void OnStart()
	{
		RebuildSectors();
		OnRaceStarted?.Invoke();

		if ( Networking.IsHost )
		{
			StartCountdownActive = true;
			startCountdownStartTime = Time.Now;
			// Seed the synced value so the first frame is "5", not a stale 0 that
			// would read as GO before the timer's first tick (HUD + SFX).
			StartCountdownTimeLeft = StartCountdownDuration;
		}
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	protected override void OnUpdate()
	{
		// Runs on EVERY machine (before the host-only gate below) so each client
		// plays the countdown SFX locally off the synced timer.
		UpdateCountdownSounds();

		if ( !Networking.IsHost )
			return;

		if ( StartCountdownActive )
		{
			float elapsed = Time.Now - startCountdownStartTime;
			StartCountdownTimeLeft = StartCountdownDuration - elapsed;

			// The instant the countdown reaches GO (TimeLeft crosses 0) the players are
			// unlocked -> start the overall race timer exactly here, once.
			if ( !raceTimerStarted && StartCountdownTimeLeft <= 0f )
				StartRaceTimer();

			if ( StartCountdownTimeLeft <= -GoDisplayDuration )
			{
				StartCountdownActive = false;
				StartCountdownTimeLeft = 0f;
			}
		}

		// Keep the live race clock ticking on the host so any HUD can read RaceTime.
		if ( raceTimerStarted && RaceTimerRunning )
			RaceTime = Time.Now - raceStartTime;

		if ( ReturnCountdownActive )
		{
			float elapsed = Time.Now - returnCountdownStartTime;
			ReturnCountdownTimeLeft = ReturnCountdownDuration - elapsed;

			if ( ReturnCountdownTimeLeft <= 0f )
			{
				ReturnCountdownActive = false;
				ReturnCountdownTimeLeft = 0f;

				if ( LobbyScene is not null )
				{
					var options = new SceneLoadOptions();
					options.SetScene( LobbyScene );
					Game.ChangeScene( options );
				}
			}
			return;
		}

		var trackers = Scene.GetAllComponents<PlayerLapTracker>().ToList();
		if ( trackers.Count == 0 )
			return;

		if ( trackers.All( t => t.RaceFinished ) )
		{
			ReturnCountdownActive = true;
			returnCountdownStartTime = Time.Now;
		}
	}

	// Which countdown step the HUD is showing right now, matching the display logic
	// in the HUD razor (MathF.Ceiling while the timer is positive, else "GO!").
	//   5,4,3,2,1 -> the on-screen number   0 -> GO   -1 -> countdown not running
	private int CountdownPhase()
	{
		if ( !StartCountdownActive )
			return -1;

		float t = StartCountdownTimeLeft;
		return t > 0f ? (int)MathF.Ceiling( t ) : 0;
	}

	// Detects when we enter a new countdown step and fires the matching local sound
	// once. We only have clips for 3, 2, 1 and GO, so 5/4 (and -1) play nothing.
	private void UpdateCountdownSounds()
	{
		int phase = CountdownPhase();
		if ( phase == lastCountdownPhase )
			return;

		int previous = lastCountdownPhase;
		lastCountdownPhase = phase;

		switch ( phase )
		{
			case 3: PlayCountdownSound( Countdown3Sound ); break;
			case 2: PlayCountdownSound( Countdown2Sound ); break;
			case 1: PlayCountdownSound( Countdown1Sound ); break;
			case 0:
				// Only when we actually counted down into GO (previous step was "1").
				// Stops a first-frame / late-join stale t==0 from mis-firing GO.
				if ( previous == 1 )
					PlayCountdownSound( GoSound );
				break;
		}
	}

	// 2D, non-positional playback -> plays on this client only (local "for myself").
	private void PlayCountdownSound( SoundEvent ev )
	{
		if ( ev is null )
			return;

		var handle = Sound.Play( ev );
		if ( handle is not null )
			handle.Volume = CountdownVolume;
	}

	public void RebuildSectors()
	{
		sectors.Clear();
		sectors.AddRange( Scene.GetAllComponents<SectorCheckpoint>() );
	}

	// Host-only. Called from the OnUpdate host block the frame the countdown reaches GO.
	private void StartRaceTimer()
	{
		raceTimerStarted = true;
		RaceTimerRunning = true;
		// Exact GO moment on the host clock (not "now", which is a frame late).
		raceStartTime = startCountdownStartTime + StartCountdownDuration;
		RaceTime = 0f;
	}

	internal void NotifyPlayerFinished( PlayerLapTracker tracker )
	{
		// Host-authoritative timing: this runs on the host (via PlayerLapTracker's
		// [Rpc.Host]). Measure the finish against the single host-clock start so every
		// player's time comes from the same origin, then try to record it as a highscore.
		if ( Networking.IsHost && tracker.IsValid() && raceTimerStarted )
		{
			float finishSeconds = Time.Now - raceStartTime;

			// Stamp the player's own overall time (replicated to everyone via the
			// FromHost sync on the tracker) so a results/HUD screen can show it.
			if ( tracker.FinishTime <= 0f )
				tracker.FinishTime = finishSeconds;

			// Steam display name of the finishing player's owner connection.
			string playerName = tracker.Network.Owner?.DisplayName;
			RaceRecordManager.SubmitTime( playerName, finishSeconds );
		}

		OnPlayerFinished?.Invoke( tracker );
	}
}
