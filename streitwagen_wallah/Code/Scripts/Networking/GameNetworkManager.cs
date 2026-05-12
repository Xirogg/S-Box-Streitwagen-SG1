using Sandbox;
using Sandbox.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;


/// <summary>
/// Erstellt automatisch eine Lobby beim Start und spawnt für jeden
/// neuen Spieler (auch den Host) das PlayerPrefab.
/// 
/// Im Editor:
/// - Component an einen leeren GameObject in der Szene hängen ("NetworkManager")
/// - PlayerPrefab: das Auto-Prefab reinziehen
/// - SpawnPoints: ein paar leere GameObjects als Spawn-Positionen reinziehen
/// </summary>

public sealed class GameNetworkManager : Component, Component.INetworkListener
{
	[Property] public GameObject PlayerChariotPrefab {  get; set; }
	[Property] public List<GameObject> PlayerSpawnPoints { get; set; } = new();

	private int _nextSpawnIndex = 0; 
	

	protected override async Task OnLoad()
	{
		if ( Scene.IsEditor )
		{
			Log.Warning( "WARNUNG" ); 
			return;
		}

		if ( !Networking.IsActive )
		{
			await Task.DelayRealtimeSeconds( 0.1f );
			Networking.CreateLobby( new LobbyConfig
			{
				MaxPlayers = 4,
				Privacy = LobbyPrivacy.Public,
				Name = "Prototyping",

			} );
		}
	}


	public void OnActive( Connection channel )
	{
		if ( PlayerChariotPrefab is null )
		{
			var lobbyObj = Scene.CreateObject( enabled: true );
			lobbyObj.Name = $"LobbyPlayer ({channel.DisplayName})";
			var lobbyPlayer = lobbyObj.Components.Create<LobbyPlayer>();
			lobbyPlayer.DisplayName = channel.DisplayName;
			lobbyObj.NetworkSpawn( channel );
			return;
		}

		var spawnPosition = Vector3.Zero;
		var spawnRotation = Rotation.Identity;

		if ( PlayerSpawnPoints is not null && PlayerSpawnPoints.Count > 0 )
		{
			var spawnPoint = PlayerSpawnPoints[_nextSpawnIndex % PlayerSpawnPoints.Count];
			_nextSpawnIndex++;

			if ( spawnPoint != null )
			{
				spawnPosition = spawnPoint.WorldPosition;
				spawnRotation = spawnPoint.WorldRotation;
			}
		}

		var player = PlayerChariotPrefab.Clone( spawnPosition, spawnRotation );
		player.Name = $"Player ({channel.DisplayName})";
		player.NetworkSpawn( channel );
	}

}
