using Sandbox;
using System.Linq;

public sealed class LobbyManager : Component
{
	public static LobbyManager Instance { get; private set; }

	[Property] public string GameScenePath { get; set; } = "scenes/egypt.scene";

	[Sync] public bool CountdownActive { get; set; }
	[Sync] public float CountdownTimeLeft { get; set; }

	private float countdownStartTime;
	private const float CountdownDuration = 5f;

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost )
			return;

		var players = Scene.GetAllComponents<LobbyPlayer>().ToList();
		Log.Info( players ); 
		if ( players.Count == 0 )
			return;

		bool allReady = players.All( p => p.IsReady );
		Log.Info( $"Lobby Update: {players.Count} players, all ready: {allReady}" );

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
				Scene.LoadFromFile( GameScenePath );
			}
		}
	}
}
