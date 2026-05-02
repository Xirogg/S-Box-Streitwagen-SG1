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
	

	protected override async Task OnLoad()
	{
		if ( Scene.IsEditor ) return;

		if ( Networking.IsActive )
		{
			await Task.DelayRealtimeSeconds( 0.1f );
			Networking.CreateLobby(); 
		}
	}


	public void OnActive( Connection channel)
	{
		if (PlayerChariotPrefab is null) return;

		var spawnPosition = Vector3.Zero;
		var spawnRotaion = Rotation.Identity; 

		if (PlayerSpawnPoints.Count > 0 && PlayerSpawnPoints is not null) 
		{

			var spawnPoint = PlayerSpawnPoints[Random.Shared.Next( PlayerSpawnPoints.Count )]; 

			if (spawnPoint != null )
			{
				spawnPosition = spawnPoint.WorldPosition;
				spawnRotaion = spawnPoint.WorldRotation; 
			}

			var player = PlayerChariotPrefab.Clone(spawnPosition, spawnRotaion);
			player.Name = $"Player ({channel.DisplayName})";
			player.NetworkSpawn( channel ); 

		}
	}

}
