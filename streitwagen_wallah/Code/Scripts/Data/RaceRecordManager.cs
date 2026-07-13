using Sandbox;
using LapSystem;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Host-autoritative Bestenliste ("Top 5 schnellste Gesamtzeiten") – PRO STRECKE.
///
/// Jede Map (siehe <see cref="RaceTrack"/>) führt ihre eigene Top-5-Liste, weil die
/// Strecken unterschiedlich lang sind. Persistenz liegt in EINER JSON-Datei in
/// <see cref="FileSystem.Data"/> auf der Host-Maschine, nach Strecken-Name aufgeschlüsselt.
///
/// Der Host ist die einzige Quelle der Wahrheit: er lädt die Datei, schreibt neue Rekorde
/// und verteilt die komplette Liste (alle Strecken) per Broadcast-RPC an alle Clients – so
/// sieht JEDER dieselben Rekorde. Der In-Memory-Store ist statisch und überlebt damit
/// Szenenwechsel (Lobby &lt;-&gt; Rennen) für die Lebensdauer des Host-Prozesses.
///
/// Auto-gespawnt via <see cref="EnsureExists"/> aus <see cref="GameNetworkManager"/> (Lobby)
/// und <see cref="RaceManager"/> (Rennen), muss also nicht in jeder Szene platziert werden.
/// </summary>
public sealed class RaceRecordManager : Component
{
	public static RaceRecordManager Instance { get; private set; }

	/// <summary> Wie viele Zeiten jede Strecke behält. </summary>
	public const int MaxRecords = 5;

	/// <summary> Dateiname in FileSystem.Data (enthält ALLE Strecken). </summary>
	public const string RecordsFileName = "race_records.json";

	// Host-autoritativer Store: Strecken-Name (RaceTrack.ToString()) -> Top-5-Liste,
	// aufsteigend nach Zeit sortiert (schnellste zuerst). Statisch -> überlebt Szenenwechsel.
	private static readonly Dictionary<string, List<RaceRecordEntry>> HostStore = new();
	private static bool hostLoaded;

	// Von allen Peers sichtbarer Spiegel der Host-Liste. Ebenfalls statisch, damit die
	// Rekorde bei einem Szenenwechsel nicht kurz "verschwinden", bevor der neue Broadcast ankommt.
	private static readonly Dictionary<string, List<RaceRecordEntry>> ClientMirror = new();

	private static readonly IReadOnlyList<RaceRecordEntry> EmptyRecords = new List<RaceRecordEntry>();

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

		// Aktuelle Liste an alle schicken (deckt frische Szenen-Ladevorgänge und spät
		// beigetretene Clients ab).
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
	/// Top <see cref="MaxRecords"/> einer bestimmten Strecke, aufsteigend nach Zeit
	/// (Platz 1 = schnellste). Auf dem Host der autoritative Store, auf Clients der
	/// replizierte Spiegel – nach einem Broadcast identisch.
	/// </summary>
	public static IReadOnlyList<RaceRecordEntry> GetRecords( RaceTrack track )
	{
		var store = Networking.IsHost ? HostStore : ClientMirror;
		return store.TryGetValue( track.ToString(), out var list ) ? list : EmptyRecords;
	}

	/// <summary> Formatiert Sekunden als "m:ss.cc" (Minuten:Sekunden.Hundertstel), z. B. "0:40.25". </summary>
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
	/// Reicht eine fertige Gesamtzeit für eine bestimmte Strecke ein. Schafft sie es in die
	/// Top <see cref="MaxRecords"/> DIESER Strecke, wird sie einsortiert, die Liste getrimmt,
	/// auf die Platte geschrieben und an alle Clients verteilt. Gibt true zurück, wenn die
	/// Zeit ein neuer Rekord war. Nur der Host führt das aus.
	/// </summary>
	public static bool SubmitTime( RaceTrack track, string playerName, float timeSeconds )
	{
		if ( !Networking.IsHost ) return false;
		if ( timeSeconds <= 0f ) return false;

		if ( !hostLoaded )
		{
			LoadFromDisk();
			hostLoaded = true;
		}

		var list = GetOrCreate( HostStore, track.ToString() );

		// Qualifiziert, wenn noch Platz ist ODER die Zeit die bisher langsamste schlägt.
		// Die Liste ist immer sortiert -> letztes Element ist die langsamste gespeicherte Zeit.
		if ( list.Count >= MaxRecords && timeSeconds >= list[list.Count - 1].TimeSeconds )
			return false;

		list.Add( new RaceRecordEntry
		{
			PlayerName = string.IsNullOrWhiteSpace( playerName ) ? "Spieler" : playerName.Trim(),
			TimeSeconds = timeSeconds,
			Date = DateTime.Now.ToString( "yyyy-MM-dd" ),
		} );

		SortAndTrim( list );
		SaveToDisk();
		PushRecords();
		return true;
	}

	/// <summary>
	/// Lädt die Bestenliste neu von der Platte und verteilt sie an alle. Für den Fall
	/// gedacht, dass die Datei extern geändert wurde – im Normalbetrieb nicht nötig.
	/// </summary>
	public static void ReloadFromDisk()
	{
		if ( !Networking.IsHost ) return;

		LoadFromDisk();
		hostLoaded = true;
		PushRecords();
	}

	/// <summary>
	/// Fordert ein Neuladen an – gedacht für "beim Öffnen der Bestenliste". Auf dem Host
	/// wird direkt von der Platte neu geladen und an alle verteilt; auf einem Client wird
	/// der Host per RPC darum gebeten (nur er hat die Datei). Danach kommt das Ergebnis
	/// über den normalen Broadcast + <see cref="OnRecordsChanged"/> zurück.
	/// </summary>
	public static void RequestReload()
	{
		if ( Networking.IsHost )
		{
			ReloadFromDisk();
			return;
		}

		Instance?.RpcRequestReloadOnHost();
	}

	[Rpc.Host]
	private void RpcRequestReloadOnHost()
	{
		ReloadFromDisk(); // läuft auf dem Host
	}

	// ---------- Internals ----------

	private static List<RaceRecordEntry> GetOrCreate( Dictionary<string, List<RaceRecordEntry>> store, string key )
	{
		if ( !store.TryGetValue( key, out var list ) )
		{
			list = new List<RaceRecordEntry>();
			store[key] = list;
		}
		return list;
	}

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
				CopyInto( HostStore, table );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[RaceRecords] Konnte '{RecordsFileName}' nicht laden: {e.Message}" );
		}
	}

	private static void SaveToDisk()
	{
		try
		{
			FileSystem.Data.WriteJson( RecordsFileName, BuildTable( HostStore ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[RaceRecords] Konnte '{RecordsFileName}' nicht speichern: {e.Message}" );
		}
	}

	// Host-only. Verteilt den kompletten HostStore (alle Strecken) an alle Peers. Wenn (noch)
	// kein vernetztes Manager-Objekt existiert, wird wenigstens der lokale Spiegel aktualisiert.
	private static void PushRecords()
	{
		var json = Json.Serialize( BuildTable( HostStore ) );

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
			CopyInto( ClientMirror, table );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[RaceRecords] Konnte Rekorde nicht lesen: {e.Message}" );
		}

		LogHighscores();
		OnRecordsChanged?.Invoke();
	}

	// Kopiert die Strecken-Listen aus einer Tabelle in einen Store (defensiv gegen null,
	// sortiert + getrimmt, damit der Store immer konsistent ist).
	private static void CopyInto( Dictionary<string, List<RaceRecordEntry>> store, RaceRecordTable table )
	{
		store.Clear();
		if ( table?.Tracks == null ) return;

		foreach ( var kv in table.Tracks )
		{
			if ( string.IsNullOrEmpty( kv.Key ) || kv.Value == null ) continue;

			var list = kv.Value.Where( e => e != null ).ToList();
			SortAndTrim( list );
			store[kv.Key] = list;
		}
	}

	private static RaceRecordTable BuildTable( Dictionary<string, List<RaceRecordEntry>> store )
	{
		var table = new RaceRecordTable();
		foreach ( var kv in store )
			table.Tracks[kv.Key] = new List<RaceRecordEntry>( kv.Value );
		return table;
	}

	// ---------- Debug ----------

	// Debug-Ausgabe ALLER Strecken-Bestenlisten. Läuft auf JEDEM Peer in dem Moment, in dem
	// die Rekorde geladen/empfangen werden (Szenen-Start und bei jedem neuen Rekord). Iteriert
	// den frisch befüllten Spiegel, damit Host UND Client dieselben Daten zeigen.
	private static void LogHighscores()
	{
		Log.Info( $"[RaceRecords] ===== Highscores (alle Strecken, Top {MaxRecords}) =====" );

		foreach ( var track in Enum.GetValues<RaceTrack>() )
		{
			string key = track.ToString();
			ClientMirror.TryGetValue( key, out var list );

			if ( list == null || list.Count == 0 )
			{
				Log.Info( $"[RaceRecords] {key}: (noch keine Rekorde)" );
				continue;
			}

			for ( int i = 0; i < list.Count; i++ )
			{
				var e = list[i];
				Log.Info( $"Place: {i + 1} | Track: {key} | Name: {e.PlayerName} | Time: {FormatTime( e.TimeSeconds )} | Date: {FormatRelativeDate( e.Date )}" );
			}
		}
	}

	/// <summary>
	/// Wandelt ein gespeichertes ISO-Datum ("yyyy-MM-dd") in einen deutschen Relativtext um:
	/// "heute", "vor 1 Tag", "vor X Tagen", "vor X Monaten", "vor X Jahren". Öffentlich, damit
	/// die Lobby-UI dasselbe Format nutzt wie der Debug-Log.
	/// </summary>
	public static string FormatRelativeDate( string isoDate )
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
