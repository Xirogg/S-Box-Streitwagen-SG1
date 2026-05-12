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

	public event Action OnRaceStarted;
	public event Action<PlayerLapTracker> OnPlayerFinished;

	[Sync] public bool ReturnCountdownActive { get; set; }
	[Sync] public float ReturnCountdownTimeLeft { get; set; }

	[Sync] public bool StartCountdownActive { get; set; }
	[Sync] public float StartCountdownTimeLeft { get; set; }

	private readonly List<SectorCheckpoint> sectors = new();
	public IReadOnlyList<SectorCheckpoint> Sectors => sectors;

	private float returnCountdownStartTime;
	private const float ReturnCountdownDuration = 6f;

	private float startCountdownStartTime;
	private const float StartCountdownDuration = 5f;
	private const float GoDisplayDuration = 1f;

	protected override void OnAwake()
	{
		Instance = this;

		if ( Networking.IsHost )
			GameObject.NetworkSpawn();
	}

	protected override void OnStart()
	{
		RebuildSectors();
		OnRaceStarted?.Invoke();

		if ( Networking.IsHost )
		{
			StartCountdownActive = true;
			startCountdownStartTime = Time.Now;
		}
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost )
			return;

		if ( StartCountdownActive )
		{
			float elapsed = Time.Now - startCountdownStartTime;
			StartCountdownTimeLeft = StartCountdownDuration - elapsed;

			if ( StartCountdownTimeLeft <= -GoDisplayDuration )
			{
				StartCountdownActive = false;
				StartCountdownTimeLeft = 0f;
			}
		}

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

	public void RebuildSectors()
	{
		sectors.Clear();
		sectors.AddRange( Scene.GetAllComponents<SectorCheckpoint>() );
	}

	internal void NotifyPlayerFinished( PlayerLapTracker tracker )
	{
		OnPlayerFinished?.Invoke( tracker );
	}
}
