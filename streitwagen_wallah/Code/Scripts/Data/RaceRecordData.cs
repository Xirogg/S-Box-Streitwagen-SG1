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
/// Gesamte Bestenliste aller Strecken – so wird die JSON-Datei aufgebaut und auch übers
/// Netzwerk verteilt. Pro Strecke eine eigene Top-5-Liste, damit unterschiedlich lange
/// Maps getrennte Rekorde führen.
///
/// <see cref="Tracks"/> ist nach Strecken-Name (RaceTrack-Enum als String, z. B. "Rom",
/// "Egypt", "Greece") aufgeschlüsselt – so bleibt die Datei gut lesbar und unabhängig von
/// der Reihenfolge/Anzahl der Enum-Werte, falls später Strecken dazukommen.
/// </summary>
public sealed class RaceRecordTable
{
	public Dictionary<string, List<RaceRecordEntry>> Tracks { get; set; } = new();
}
