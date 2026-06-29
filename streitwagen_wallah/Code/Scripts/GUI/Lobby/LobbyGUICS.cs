using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Sandbox.Scripts.GUI.Lobby;   // LobbyGUI-Component (Razor)

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

	// Strongly-typed Texture properties. The asset pipeline's reference scanner
	// can ONLY see references like this — it cannot see a "/ui/tracks/..." string
	// built at runtime inside the razor's TrackImagePath getter. Without these
	// references the PNGs aren't bundled into the package that joining clients
	// download, so the host (running the editor with all files on local disk)
	// sees the previews but everyone else gets a blank box. Assign these in the
	// inspector to the same PNGs the URL switch used to point at.
	[Property] public Texture RomTrackImage { get; set; }
	[Property] public Texture AegyptenTrackImage { get; set; }
	[Property] public Texture AkropolisTrackImage { get; set; }

	// UI-Screens. Im Inspector die jeweilige Component reinziehen (LobbyGUI vom
	// "-- GUI"-Node, AltarGUI vom "AltarGUIScreen"-Node). Es werden die
	// COMPONENTS getoggelt (nicht die GameObjects), damit das ScreenPanel und der
	// Hintergrund aktiv bleiben, auch wenn die LobbyGUI auf demselben Node liegt.
	[Property, Group( "Screens" )] public LobbyGUI LobbyScreen { get; set; }
	[Property, Group( "Screens" )] public AltarGUI AltarScreen { get; set; }

	// Was un-synced; only RpcSetTrack pushed it, so a client joining after the
	// host had already cycled the track would default to Rom forever. [Sync]
	// makes the current value part of the owner-authoritative state every joiner
	// receives on connect.
	[Sync] public TrackSelection SelectedTrack { get; set; } = TrackSelection.Rom;
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

	/// <summary>
	/// URL for a track preview, derived from the strongly-typed Texture property
	/// the inspector wires up. Using <see cref="Resource.ResourcePath"/> instead
	/// of a hand-written literal keeps the path in lockstep with what the asset
	/// pipeline actually bundled, and the Texture reference itself is what
	/// forces the PNG to be included in the package shipped to joining clients.
	/// </summary>
	public string GetTrackImagePath( TrackSelection track )
	{
		return GetTrackTexture( track )?.ResourcePath ?? "";
	}

	/// <summary>
	/// Strecken-Vorschau als Texture-Objekt. Wird von der LobbyGUI direkt per
	/// Style.SetBackgroundImage gesetzt (unabhängig vom ResourcePath, siehe
	/// Kommentar in LobbyGUI.razor).
	/// </summary>
	public Texture GetTrackTexture( TrackSelection track )
	{
		return track switch
		{
			TrackSelection.Rom => RomTrackImage,
			TrackSelection.Aegypten => AegyptenTrackImage,
			TrackSelection.Akropolis => AkropolisTrackImage,
			_ => null
		};
	}

	/// <summary>
	/// Wechselt vom Lobby- auf den Altar-Screen: LobbyGUI-Component aus,
	/// AltarGUI-Component an. Rein lokale UI-Umschaltung (kein Networking).
	/// </summary>
	public void ShowAltar() => SwitchScreen( AltarScreen, LobbyScreen );

	/// <summary>Zurück zum Lobby-Screen (Gegenstück zu <see cref="ShowAltar"/>).</summary>
	public void ShowLobby() => SwitchScreen( LobbyScreen, AltarScreen );

	// Aktiviert "show", deaktiviert "hide". Nur wenn BEIDE zugewiesen sind, sonst
	// säße man nach dem Ausschalten auf einem leeren Screen fest.
	private void SwitchScreen( PanelComponent show, PanelComponent hide )
	{
		Log.Info( $"[LobbyManager] SwitchScreen: show={(show is null ? "NULL" : show.GetType().Name)}, " +
			$"hide={(hide is null ? "NULL" : hide.GetType().Name)}" );

		if ( show is null || hide is null )
		{
			Log.Warning( "[LobbyManager] Screen-Wechsel: LobbyScreen/AltarScreen nicht (beide) im Inspector zugewiesen." );
			return;
		}

		hide.Enabled = false;
		show.Enabled = true;
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
