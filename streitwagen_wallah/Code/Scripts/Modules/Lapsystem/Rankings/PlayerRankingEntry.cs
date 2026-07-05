using System;

namespace LapSystem.Rankings;

/// <summary>
/// Snapshot eines Spielers für die Renn-Platzierung. Wird vom RaceRankingManager
/// jeden Tick neu befüllt. Kein Component – reines Datenobjekt.
/// </summary>
public sealed class PlayerRankingEntry
{
	/// <summary> Stabile Spieler-Identität (aus TestControlls.PlayerId, sonst GameObject.Id). </summary>
	public Guid PlayerId { get; init; }

	/// <summary> Referenz auf den Tracker. Kann null werden, wenn der Spieler die Szene verlässt. </summary>
	public PlayerLapTracker Tracker { get; set; }

	/// <summary> Anzeigename des Spielers (aus der Owner-Connection). Für die Leaderboard-UI. </summary>
	public string PlayerName { get; set; }

	/// <summary> 1-basierte Platzierung. Wird vom Manager nach jedem Sortier-Pass gesetzt. </summary>
	public int Position { get; set; }

	public int CurrentLap { get; set; }
	public int CheckpointsThisLap { get; set; }
	public float LastProgressTime { get; set; }
	public bool RaceFinished { get; set; }

	/// <summary> 0 = noch nicht im Ziel. Sonst 1, 2, 3, ... in der Reihenfolge des Zieleinlaufs. </summary>
	public int FinishOrder { get; set; }
}
