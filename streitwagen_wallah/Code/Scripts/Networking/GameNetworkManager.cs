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

	// 8 visually distinct player colors — Can be changed to whatever
	private static readonly Color[] PlayerColorPalette = new[]
	{
		new Color( 0.95f, 0.20f, 0.20f ), // red
		new Color( 0.20f, 0.45f, 0.95f ), // blue
		new Color( 1.00f, 0.85f, 0.10f ), // yellow
		new Color( 0.20f, 0.80f, 0.30f ), // green
		new Color( 1.00f, 0.50f, 0.10f ), // orange
		new Color( 0.65f, 0.25f, 0.85f ), // purple
		new Color( 0.10f, 0.85f, 0.85f ), // cyan
		new Color( 1.00f, 0.40f, 0.75f ), // pink
	};

	private readonly Queue<Color> _availableColors = new();



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

		PublicityCurrencyManager.EnsureExists( Scene );
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

		ApplyPlayerColor( player );

		player.NetworkSpawn( channel );
	}

	private void ApplyPlayerColor( GameObject player )
	{
		// Lazy-init the shuffled color pool on first use so we don't allocate during OnLoad.
		if ( _availableColors.Count == 0 && PlayerColorPalette.Length > 0 )
		{
			var shuffled = PlayerColorPalette.OrderBy( _ => Random.Shared.Next() ).ToList();
			foreach ( var c in shuffled )
				_availableColors.Enqueue( c );
		}

		// Pop one color — fall back to a random tint 
		Color color = _availableColors.Count > 0
			? _availableColors.Dequeue().WithAlpha( 1f )
			: Color.Random.WithAlpha( 1f );

		// Only tint renderers that sit on a "chonk"-tagged GameObject. 
		foreach ( var renderer in player.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( !renderer.GameObject.Tags.Has( "chonk" ) ) continue;
			renderer.Tint = color;
		}
	}

}
