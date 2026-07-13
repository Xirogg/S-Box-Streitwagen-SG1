using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>
/// Wire-/Datei-Format für die Namenszuordnung: SteamId (als String) -> selbst gewählter
/// Anzeigename. Eigene Klasse, damit System.Text.Json es sauber (de)serialisiert.
/// </summary>
public sealed class PlayerNameTable
{
	public Dictionary<string, string> Names { get; set; } = new();
}

/// <summary>
/// Zentrale Quelle der Wahrheit für ANZEIGENAMEN. Intern bleibt alles bei der SteamId –
/// nur der angezeigte Name kann pro Spieler überschrieben werden (Custom-Nickname). Ohne
/// eigenen Namen wird der Steam-Name als Default benutzt.
///
/// Aufbau wie die anderen Manager (host-autoritativ, [Rpc.Broadcast]-Spiegel, statischer
/// Store der Szenenwechsel überlebt – siehe [[project-overview]] / PublicityCurrencyManager):
///   - Host hält die maßgebliche SteamId->Name-Tabelle und verteilt sie an alle.
///   - JEDER Client speichert SEINEN EIGENEN Namen lokal (FileSystem.Data) und meldet ihn
///     beim Start an den Host. Dadurch überlebt der eigene Name auch einen Neustart / einen
///     Host-Wechsel, ohne dass der Host fremde Namen dauerhaft speichern müsste.
///
/// Auto-gespawnt via <see cref="EnsureExists"/> aus <see cref="GameNetworkManager"/> (Lobby)
/// und <see cref="LapSystem.RaceManager"/> (Rennen).
/// </summary>
public sealed class PlayerNameManager : Component
{
	public static PlayerNameManager Instance { get; private set; }

	public const int MaxNameLength = 20;

	/// <summary> Datei in FileSystem.Data mit dem NUR-LOKAL gewählten Namen dieses Rechners. </summary>
	public const string LocalNameFile = "player_nickname.txt";

	// Host-autoritativ: SteamId -> Custom-Name. Statisch -> überlebt Szenenwechsel.
	private static readonly Dictionary<ulong, string> HostStore = new();

	// Von allen Peers sichtbarer Spiegel.
	private static readonly Dictionary<ulong, string> ClientMirror = new();

	/// <summary> Feuert auf JEDEM Peer, wenn sich Namen geändert haben (für UI-Refresh). </summary>
	public static event Action OnNamesChanged;

	/// <summary>
	/// True, solange der lokale Spieler gerade seinen Namen im Textfeld eintippt. Andere
	/// Skripte (LobbyPlayer, LobbyGUI) pausieren dann ihre Hotkeys (Space/Tab/Entf), damit
	/// das Tippen nicht "Bereit"/"Strecke wechseln"/"Beenden" auslöst.
	/// </summary>
	public static bool LocalNameInputActive { get; set; }

	// ---------- Lifecycle ----------

	protected override void OnAwake()
	{
		Instance = this;

		if ( Networking.IsHost )
			GameObject.NetworkSpawn();
	}

	protected override void OnStart()
	{
		// Host: verteilt die aktuell bekannten Namen (deckt Szenen-Ladevorgänge / Joiner ab).
		if ( Networking.IsHost )
			PushNames();

		// Jeder Peer: den eigenen, lokal gespeicherten Namen (falls vorhanden) beim Host
		// anmelden. So ist der Name nach Szenenwechsel/Neustart wieder da.
		AnnounceLocalNameFromDisk();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	/// <summary> Idempotent: legt (nur auf dem Host) einen Manager an, falls keiner existiert. </summary>
	public static void EnsureExists( Scene scene )
	{
		if ( !Networking.IsHost ) return;
		if ( Instance.IsValid() ) return;
		if ( scene is null ) return;

		var go = scene.CreateObject( enabled: true );
		go.Name = "PlayerNameManager";
		go.Components.Create<PlayerNameManager>();
	}

	// ---------- Read API (jeder Peer) ----------

	/// <summary>
	/// Effektiver Anzeigename einer Connection: Custom-Name, falls gesetzt, sonst der
	/// Steam-Name. Fallback "Spieler", wenn gar nichts auflösbar ist.
	/// </summary>
	public static string GetDisplayName( Connection conn )
	{
		if ( conn is null )
			return "Spieler";

		ulong id = conn.SteamId;
		var store = Networking.IsHost ? HostStore : ClientMirror;
		if ( store.TryGetValue( id, out var custom ) && !string.IsNullOrWhiteSpace( custom ) )
			return custom;

		return string.IsNullOrWhiteSpace( conn.DisplayName ) ? "Spieler" : conn.DisplayName;
	}

	/// <summary> Effektiver Anzeigename des lokalen Spielers. </summary>
	public static string LocalDisplayName => GetDisplayName( Connection.Local );

	/// <summary> Der aktuell gesetzte Custom-Name des lokalen Spielers, oder "" wenn keiner. </summary>
	public static string LocalCustomNameOrEmpty
	{
		get
		{
			ulong id = Connection.Local?.SteamId ?? 0UL;
			if ( id == 0 ) return "";

			var store = Networking.IsHost ? HostStore : ClientMirror;
			return store.TryGetValue( id, out var n ) ? (n ?? "") : "";
		}
	}

	// ---------- Write API (lokaler Spieler setzt seinen eigenen Namen) ----------

	/// <summary>
	/// Setzt den Namen des LOKALEN Spielers. Wird lokal gespeichert (überlebt Neustart) und
	/// an den Host gemeldet, der ihn an alle verteilt. Leerer Name = Custom-Name löschen
	/// (zurück zum Steam-Namen).
	/// </summary>
	public static void SetLocalName( string name )
	{
		ulong id = Connection.Local?.SteamId ?? 0UL;
		if ( id == 0 ) return;

		name = Sanitize( name );

		SaveLocalNameToDisk( name );
		SendNameToHost( id, name );
	}

	// ---------- Internals ----------

	private static string Sanitize( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return "";
		name = name.Trim();
		if ( name.Length > MaxNameLength )
			name = name.Substring( 0, MaxNameLength );
		return name;
	}

	private void AnnounceLocalNameFromDisk()
	{
		string name = LoadLocalNameFromDisk();
		if ( string.IsNullOrWhiteSpace( name ) ) return;

		ulong id = Connection.Local?.SteamId ?? 0UL;
		SendNameToHost( id, name );
	}

	// Host wendet direkt an; Client bittet den Host per RPC.
	private static void SendNameToHost( ulong steamId, string name )
	{
		if ( steamId == 0 ) return;

		if ( Networking.IsHost )
			ApplyName( steamId, name );
		else
			Instance?.RpcSetNameOnHost( steamId, name );
	}

	[Rpc.Host]
	private void RpcSetNameOnHost( ulong steamId, string name )
	{
		ApplyName( steamId, Sanitize( name ) );
	}

	private static void ApplyName( ulong steamId, string name )
	{
		if ( !Networking.IsHost ) return;
		if ( steamId == 0 ) return;

		bool changed;
		if ( string.IsNullOrWhiteSpace( name ) )
			changed = HostStore.Remove( steamId );   // gelöscht -> zurück zum Steam-Namen
		else
		{
			HostStore.TryGetValue( steamId, out var old );
			changed = old != name;
			HostStore[steamId] = name;
		}

		if ( changed )
			PushNames();
	}

	// ---------- Netzwerk-Verteilung ----------

	private static void PushNames()
	{
		var table = new PlayerNameTable();
		foreach ( var kv in HostStore )
			table.Names[kv.Key.ToString()] = kv.Value;

		var json = Json.Serialize( table );

		var mgr = Instance;
		if ( mgr.IsValid() )
			mgr.RpcSetNames( json ); // Broadcast läuft auch lokal auf dem Host
		else
			ApplyNamesFromJson( json );
	}

	[Rpc.Broadcast]
	private void RpcSetNames( string json )
	{
		ApplyNamesFromJson( json );
	}

	private static void ApplyNamesFromJson( string json )
	{
		ClientMirror.Clear();

		try
		{
			var table = Json.Deserialize<PlayerNameTable>( json );
			if ( table?.Names != null )
			{
				foreach ( var kv in table.Names )
				{
					if ( ulong.TryParse( kv.Key, out var id ) && !string.IsNullOrWhiteSpace( kv.Value ) )
						ClientMirror[id] = kv.Value;
				}
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[PlayerNames] Konnte Namen nicht lesen: {e.Message}" );
		}

		OnNamesChanged?.Invoke();
	}

	// ---------- Lokale Persistenz (nur der eigene Name, pro Rechner) ----------

	private static string LoadLocalNameFromDisk()
	{
		try
		{
			if ( FileSystem.Data.FileExists( LocalNameFile ) )
				return (FileSystem.Data.ReadAllText( LocalNameFile ) ?? "").Trim();
		}
		catch ( Exception e )
		{
			Log.Warning( $"[PlayerNames] Konnte '{LocalNameFile}' nicht laden: {e.Message}" );
		}
		return "";
	}

	private static void SaveLocalNameToDisk( string name )
	{
		try
		{
			FileSystem.Data.WriteAllText( LocalNameFile, name ?? "" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[PlayerNames] Konnte '{LocalNameFile}' nicht speichern: {e.Message}" );
		}
	}
}
