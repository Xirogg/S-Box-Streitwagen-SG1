using System.Collections.Generic;

/// <summary>
/// Ein einzelner Highscore-Eintrag (eine der Top-Zeiten). Reines, JSON-serialisierbares
/// Datenobjekt – kein Component. Wird sowohl in die Datei (FileSystem.Data) geschrieben
/// als auch (per JSON-String) über das Netzwerk an alle Clients verteilt.
/// </summary>
public sealed class RaceRecordEntry
{
	/// <summary> Anzeigename des Spielers (Steam-Name) zum Zeitpunkt der Bestzeit. </summary>
	public string PlayerName { get; set; } = "";

	/// <summary> Gesamt-Rennzeit in Sekunden. Kleiner = schneller = besser. </summary>
	public float TimeSeconds { get; set; }

	/// <summary> Datum, an dem die Zeit gesetzt wurde (ISO "yyyy-MM-dd"). </summary>
	public string Date { get; set; } = "";
}

/// <summary>
/// Container um die Bestenliste. Als eigene Klasse (statt bloßer List) gehalten, damit
/// die Datei später problemlos um Metadaten (z. B. Version, Streckenname) erweitert
/// werden kann, ohne das Format zu brechen.
/// </summary>
public sealed class RaceRecordTable
{
	public List<RaceRecordEntry> Entries { get; set; } = new();
}
