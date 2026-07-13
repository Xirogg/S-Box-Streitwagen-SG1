using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Host-autoritative Bestenliste ("Top 5 schnellste Gesamtzeiten").
///
/// Persistenz liegt in einer JSON-Datei in <see cref="FileSystem.Data"/> auf der
/// Host-Maschine. Der Host ist die einzige Quelle der Wahrheit: er lädt die Datei,
/// schreibt neue Rekorde und verteilt die aktuelle Liste per Broadcast-RPC an alle
/// Clients – so sieht JEDER dieselben Top 5 (auch spät beitretende Spieler, weil der
/// Host bei jedem Szenen-Start erneut broadcastet).
///
/// Der In-Memory-Store ist statisch und überlebt damit Szenenwechsel (Lobby &lt;-&gt;
/// Rennen) für die Lebensdauer des Host-Prozesses – exakt dasselbe Muster wie beim
/// <see cref="PublicityCurrencyManager"/>.
///
/// Auto-gespawnt via <see cref="EnsureExists"/> aus <see cref="GameNetworkManager"/>
/// (Lobby) und <see cref="LapSystem.RaceManager"/> (Rennen), muss also nicht in jeder
/// Szene manuell platziert werden.
/// </summary>
public sealed class RaceRecordManager : Component
{
	public static RaceRecordManager Instance { get; private set; }

	/// <summary> Wie viele Zeiten die Bestenliste behält. </summary>
	public const int MaxRecords = 5;

	/// <summary> Dateiname in FileSystem.Data. </summary>
	public const string RecordsFileName = "race_records.json";

	// Host-autoritativer Store, aufsteigend nach Zeit sortiert (schnellste zuerst).
	// Statisch -> überlebt Szenenwechsel im Host-Prozess.
	private static readonly List<RaceRecordEntry> HostStore = new();
	private static bool hostLoaded;

	// Von allen Peers sichtbarer Spiegel der Host-Liste. Ebenfalls statisch, damit die
	// Rekorde bei einem Szenenwechsel nicht kurz "verschwinden", bevor der neue
	// Broadcast ankommt.
	private static readonly List<RaceRecordEntry> ClientMirror = new();

	/// <summary> Feuert auf JEDEM Peer, sobald sich die Bestenliste geändert hat (für UI). </summary>
	public static event Action OnRecordsChanged;

	// ---------- Lifecycle ----------

	protected override void OnAwake()
	{
		Instance = this;

		if ( Networking.IsHost )
			GameObject.NetworkSpawn();
	}

	protected override void OnStart()
	{
		if ( !Networking.IsHost )
			return;

		// Beim allerersten Mal von der Platte laden; danach ist HostStore die Wahrheit
		// und wird bei jedem Schreiben mit der Datei synchron gehalten.
		if ( !hostLoaded )
		{
			LoadFromDisk();
			hostLoaded = true;
		}

		// Aktuelle Liste an alle schicken (deckt frische Szenen-Ladevorgänge und
		// spät beigetretene Clients ab).
		PushRecords();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	/// <summary>
	/// Idempotent: legt (nur auf dem Host) einen Manager in der Szene an, falls keiner
	/// existiert. Von jedem Peer aufrufbar; nur der Host erzeugt tatsächlich das Objekt.
	/// </summary>
	public static void EnsureExists( Scene scene )
	{
		if ( !Networking.IsHost ) return;
		if ( Instance.IsValid() ) return;
		if ( scene is null ) return;

		var go = scene.CreateObject( enabled: true );
		go.Name = "RaceRecordManager";
		go.Components.Create<RaceRecordManager>();
	}

	// ---------- Read API (jeder Peer) ----------

	/// <summary>
	/// Aktuelle Bestenliste, aufsteigend nach Zeit (Platz 1 = schnellste). Auf dem Host
	/// der autoritative Store, auf Clients der replizierte Spiegel – nach einem Broadcast
	/// identisch.
	/// </summary>
	public static IReadOnlyList<RaceRecordEntry> Records
		=> Networking.IsHost ? HostStore : ClientMirror;

	/// <summary>
	/// Formatiert Sekunden als "m:ss.cc" (Minuten:Sekunden.Hundertstel), z. B. 40,25 s -> "0:40.25".
	/// Über Hundertstel gerechnet (nicht über einen String-Format wie "00.00"), damit
	/// Randfälle wie 59,999 sauber zu "1:00.00" aufrunden statt "0:60.00" zu zeigen.
	/// </summary>
	public static string FormatTime( float seconds )
	{
		if ( seconds < 0f ) seconds = 0f;

		int totalHundredths = (int)MathF.Round( seconds * 100f );
		int minutes = totalHundredths / 6000;
		int secs = (totalHundredths / 100) % 60;
		int hundredths = totalHundredths % 100;

		return $"{minutes}:{secs:00}.{hundredths:00}";
	}

	// ---------- Write API (host-only; Aufrufe auf Clients sind No-Ops) ----------

	/// <summary>
	/// Reicht eine fertige Gesamtzeit ein. Schafft sie es in die Top <see cref="MaxRecords"/>,
	/// wird sie einsortiert, die Liste getrimmt, auf die Platte geschrieben und an alle
	/// Clients verteilt. Gibt true zurück, wenn die Zeit ein neuer Rekord war.
	/// Nur der Host führt das aus.
	/// </summary>
	public static bool SubmitTime( string playerName, float timeSeconds )
	{
		if ( !Networking.IsHost ) return false;
		if ( timeSeconds <= 0f ) return false;

		if ( !hostLoaded )
		{
			LoadFromDisk();
			hostLoaded = true;
		}

		// Qualifiziert, wenn noch Platz ist ODER die Zeit die bisher langsamste schlägt.
		// HostStore ist immer sortiert -> letztes Element ist die langsamste gespeicherte Zeit.
		if ( HostStore.Count >= MaxRecords && timeSeconds >= HostStore[HostStore.Count - 1].TimeSeconds )
			return false;

		HostStore.Add( new RaceRecordEntry
		{
			PlayerName = string.IsNullOrWhiteSpace( playerName ) ? "Spieler" : playerName.Trim(),
			TimeSeconds = timeSeconds,
			Date = DateTime.Now.ToString( "yyyy-MM-dd" ),
		} );

		SortAndTrim( HostStore );
		SaveToDisk();
		PushRecords();
		return true;
	}

	/// <summary>
	/// Lädt die Bestenliste neu von der Platte und verteilt sie an alle. Für den Fall
	/// gedacht, dass die Datei extern geändert wurde – im Normalbetrieb nicht nötig,
	/// weil der Host-Store bereits die aktuelle Liste hält.
	/// </summary>
	public static void ReloadFromDisk()
	{
		if ( !Networking.IsHost ) return;

		LoadFromDisk();
		hostLoaded = true;
		PushRecords();
	}

	// ---------- Internals ----------

	private static void SortAndTrim( List<RaceRecordEntry> list )
	{
		list.Sort( ( a, b ) => a.TimeSeconds.CompareTo( b.TimeSeconds ) );
		if ( list.Count > MaxRecords )
			list.RemoveRange( MaxRecords, list.Count - MaxRecords );
	}

	private static void LoadFromDisk()
	{
		HostStore.Clear();

		try
		{
			if ( FileSystem.Data.FileExists( RecordsFileName ) )
			{
				var table = FileSystem.Data.ReadJson<RaceRecordTable>( RecordsFileName, new RaceRecordTable() );
				if ( table?.Entries != null )
					HostStore.AddRange( table.Entries.Where( e => e != null ) );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[RaceRecords] Konnte '{RecordsFileName}' nicht laden: {e.Message}" );
		}

		SortAndTrim( HostStore );
	}

	private static void SaveToDisk()
	{
		try
		{
			var table = new RaceRecordTable { Entries = new List<RaceRecordEntry>( HostStore ) };
			FileSystem.Data.WriteJson( RecordsFileName, table );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[RaceRecords] Konnte '{RecordsFileName}' nicht speichern: {e.Message}" );
		}
	}

	// Host-only. Verteilt den aktuellen HostStore an alle Peers. Wenn (noch) kein
	// vernetztes Manager-Objekt existiert, wird wenigstens der lokale Spiegel aktualisiert.
	private static void PushRecords()
	{
		var table = new RaceRecordTable { Entries = new List<RaceRecordEntry>( HostStore ) };
		var json = Json.Serialize( table );

		var mgr = Instance;
		if ( mgr.IsValid() )
			mgr.RpcSetRecords( json ); // Broadcast läuft auch lokal auf dem Host -> Spiegel + Event überall
		else
			ApplyRecordsFromJson( json );
	}

	[Rpc.Broadcast]
	private void RpcSetRecords( string json )
	{
		ApplyRecordsFromJson( json );
	}

	private static void ApplyRecordsFromJson( string json )
	{
		ClientMirror.Clear();

		try
		{
			var table = Json.Deserialize<RaceRecordTable>( json );
			if ( table?.Entries != null )
				ClientMirror.AddRange( table.Entries.Where( e => e != null ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[RaceRecords] Konnte Rekorde nicht lesen: {e.Message}" );
		}

		LogHighscores();
		OnRecordsChanged?.Invoke();
	}

	// Debug-Ausgabe der kompletten Bestenliste. Läuft auf JEDEM Peer in dem Moment, in
	// dem die Rekorde geladen/empfangen werden (also beim Szenen-Start und bei jedem
	// neuen Rekord). Iteriert den frisch befüllten Spiegel, damit es auf Host UND Client
	// dieselben Daten zeigt.
	private static void LogHighscores()
	{
		Log.Info( $"[RaceRecords] ===== Highscores (Top {MaxRecords}) =====" );

		if ( ClientMirror.Count == 0 )
		{
			Log.Info( "[RaceRecords] (noch keine Rekorde gespeichert)" );
			return;
		}

		for ( int i = 0; i < ClientMirror.Count; i++ )
		{
			var e = ClientMirror[i];
			Log.Info( $"Place: {i + 1} | Name: {e.PlayerName} | Time: {FormatTime( e.TimeSeconds )} | Date: {RelativeDateGerman( e.Date )}" );
		}
	}

	// Wandelt ein gespeichertes ISO-Datum ("yyyy-MM-dd") in einen deutschen
	// Relativtext um: "heute", "vor 1 Tag", "vor X Tagen", "vor X Monaten", "vor X Jahren".
	private static string RelativeDateGerman( string isoDate )
	{
		if ( !TryParseIsoDate( isoDate, out var date ) )
			return string.IsNullOrWhiteSpace( isoDate ) ? "unbekannt" : isoDate;

		int days = (int)(DateTime.Now.Date - date.Date).TotalDays;

		if ( days <= 0 ) return "heute";
		if ( days == 1 ) return "vor 1 Tag";

		if ( days >= 365 )
		{
			int years = days / 365;
			return years == 1 ? "vor 1 Jahr" : $"vor {years} Jahren";
		}
		if ( days >= 30 )
		{
			int months = days / 30;
			return months == 1 ? "vor 1 Monat" : $"vor {months} Monaten";
		}
		return $"vor {days} Tagen";
	}

	// Manuelles Parsen (statt DateTime.Parse), um unabhängig von Kultur/Whitelist zu sein.
	private static bool TryParseIsoDate( string s, out DateTime date )
	{
		date = default;
		if ( string.IsNullOrWhiteSpace( s ) ) return false;

		var parts = s.Split( '-' );
		if ( parts.Length != 3 ) return false;
		if ( !int.TryParse( parts[0], out int y ) ) return false;
		if ( !int.TryParse( parts[1], out int m ) ) return false;
		if ( !int.TryParse( parts[2], out int d ) ) return false;

		try { date = new DateTime( y, m, d ); return true; }
		catch { return false; }
	}
}
