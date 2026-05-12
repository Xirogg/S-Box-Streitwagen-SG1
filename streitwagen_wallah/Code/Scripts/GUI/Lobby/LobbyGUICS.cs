using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

public enum TrackSelection
{
	Rom,
	Aegypten,
	Akropolis
}

public sealed class LobbyManager : Component
{
	public static LobbyManager Instance { get; private set; }

	[Property] public SceneFile RomScene { get; set; }
	[Property] public SceneFile AegyptenScene { get; set; }
	[Property] public SceneFile AkropolisScene { get; set; }

	public TrackSelection SelectedTrack { get; set; } = TrackSelection.Rom;
	[Sync] public bool CountdownActive { get; set; }
	[Sync] public float CountdownTimeLeft { get; set; }

	private float countdownStartTime;
	private const float CountdownDuration = 5f;

	protected override void OnAwake()
	{
		Instance = this;

		if ( Networking.IsHost )
			GameObject.NetworkSpawn();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	public SceneFile GetSelectedSceneFile()
	{
		return SelectedTrack switch
		{
			TrackSelection.Rom => RomScene,
			TrackSelection.Aegypten => AegyptenScene,
			TrackSelection.Akropolis => AkropolisScene,
			_ => RomScene
		};
	}

	public void CycleTrack()
	{
		if ( !Networking.IsHost )
			return;

		var values = Enum.GetValues<TrackSelection>();
		int next = ( (int)SelectedTrack + 1 ) % values.Length;
		RpcSetTrack( (TrackSelection)next );
	}

	[Rpc.Broadcast]
	public void RpcSetTrack( TrackSelection track )
	{
		SelectedTrack = track;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost )
			return;

		var players = Scene.GetAllComponents<LobbyPlayer>().ToList();
		if ( players.Count == 0 )
			return;

		bool allReady = players.All( p => p.IsReady );

		if ( allReady && !CountdownActive )
		{
			CountdownActive = true;
			countdownStartTime = Time.Now;
		}

		if ( !allReady && CountdownActive )
		{
			CountdownActive = false;
			CountdownTimeLeft = 0f;
		}

		if ( CountdownActive )
		{
			float elapsed = Time.Now - countdownStartTime;
			CountdownTimeLeft = CountdownDuration - elapsed;

			if ( CountdownTimeLeft <= 0f )
			{
				CountdownActive = false;
				CountdownTimeLeft = 0f;

				var sceneFile = GetSelectedSceneFile();
				if ( sceneFile is not null )
				{
					var options = new SceneLoadOptions();
					options.SetScene( sceneFile );
					Game.ChangeScene( options );
				}
			}
		}
	}
}
