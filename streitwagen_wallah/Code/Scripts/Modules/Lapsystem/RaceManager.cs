using System;
using System.Collections.Generic;
using Sandbox;

namespace LapSystem;

/// <summary>
/// Eine Instanz pro Szene. Hält die globalen Renn-Einstellungen
/// (z. B. MaxLaps) und kennt alle SectorCheckpoints im Track.
/// Per-Player-State (Rundenzahl, abgehakte Sektoren) liegt im PlayerLapTracker.
/// </summary>
public sealed class RaceManager : Component
{
	public static RaceManager Instance { get; private set; }

	[Property] public int MaxLaps { get; set; } = 3;

	/// <summary> Wird gefeuert, sobald die Szene das Rennen aufgesetzt hat. </summary>
	public event Action OnRaceStarted;

	/// <summary> Wird gefeuert, wenn ein einzelner Spieler das Rennen abgeschlossen hat. </summary>
	public event Action<PlayerLapTracker> OnPlayerFinished;

	private readonly List<SectorCheckpoint> sectors = new();
	public IReadOnlyList<SectorCheckpoint> Sectors => sectors;

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnStart()
	{
		// Sektoren einmalig sammeln. Falls du Sektoren zur Laufzeit hinzufügst,
		// einfach erneut RebuildSectors() aufrufen.
		RebuildSectors();
		OnRaceStarted?.Invoke();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	public void RebuildSectors()
	{
		sectors.Clear();
		sectors.AddRange( Scene.GetAllComponents<SectorCheckpoint>() );
	}

	internal void NotifyPlayerFinished( PlayerLapTracker tracker )
	{
		OnPlayerFinished?.Invoke( tracker );
	}
}
