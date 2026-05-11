using System;
using System.Collections.Generic;
using Sandbox;

namespace LapSystem.Rankings;

/// <summary>
/// Berechnet die Live-Platzierungen aller Spieler im Rennen.
/// Eine Instanz pro Szene, neben dem RaceManager.
///
/// Liest die [Sync]-Felder von PlayerLapTracker (CurrentLap, CheckpointsThisLap,
/// LastProgressTime, RaceFinished) – das Ranking entsteht also auf jedem Client
/// lokal aus bereits replizierten Daten. FinishOrder wird host-autoritativ vergeben
/// und ebenfalls über den synchronisierten RaceFinished-Flag plus die Reihenfolge
/// der OnPlayerFinished-Events stabilisiert.
/// </summary>
public sealed class RaceRankingManager : Component
{
	public static RaceRankingManager Instance { get; private set; }

	/// <summary> Feuert, wenn sich die sortierte Reihenfolge der Spieler geändert hat. </summary>
	public event Action OnRankingsUpdated;

	/// <summary> Wie oft pro Sekunde das Ranking neu sortiert wird. 5 Hz reicht für ein HUD. </summary>
	[Property] public float UpdateInterval { get; set; } = 0.2f;

	private readonly Dictionary<Guid, PlayerRankingEntry> entriesById = new();
	private readonly List<PlayerRankingEntry> sorted = new();

	/// <summary> Aktuelle Platzierung, sortiert von Platz 1 absteigend. </summary>
	public IReadOnlyList<PlayerRankingEntry> Rankings => sorted;

	// Host-only – stabile Zähl-Quelle für FinishOrder
	private int nextFinishOrder = 1;
	private float nextRebuildTime;

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnDestroy()
	{
		var rm = RaceManager.Instance;
		if ( rm != null )
			rm.OnPlayerFinished -= HandlePlayerFinished;

		if ( Instance == this )
			Instance = null;
	}

	protected override void OnStart()
	{
		var rm = RaceManager.Instance;
		if ( rm != null )
			rm.OnPlayerFinished += HandlePlayerFinished;
	}

	private void HandlePlayerFinished( PlayerLapTracker tracker )
	{
		if ( !Networking.IsHost ) return;
		if ( tracker == null ) return;

		var entry = FindOrCreateEntry( tracker );
		if ( entry.FinishOrder == 0 )
			entry.FinishOrder = nextFinishOrder++;
	}

	protected override void OnUpdate()
	{
		if ( Time.Now < nextRebuildTime ) return;
		nextRebuildTime = Time.Now + UpdateInterval;

		RebuildAndSort();
	}

	private void RebuildAndSort()
	{
		// 1. Alle aktuellen Tracker einsammeln und Einträge aktualisieren / anlegen.
		var seen = new HashSet<Guid>();

		foreach ( var tracker in Scene.GetAllComponents<PlayerLapTracker>() )
		{
			if ( tracker == null || !tracker.IsValid() ) continue;

			var entry = FindOrCreateEntry( tracker );
			seen.Add( entry.PlayerId );

			entry.Tracker = tracker;
			entry.CurrentLap = tracker.CurrentLap;
			entry.CheckpointsThisLap = tracker.CheckpointsThisLap;
			entry.LastProgressTime = tracker.LastProgressTime;

			// FinishOrder kommt vom Host-Event; auf Clients leiten wir aus RaceFinished
			// einen Platzhalter-Wert ab, falls das Event noch nicht angekommen ist.
			if ( tracker.RaceFinished && !entry.RaceFinished )
				entry.RaceFinished = true;
			else
				entry.RaceFinished = tracker.RaceFinished;
		}

		// 2. Verwaiste Einträge entfernen (Spieler hat Szene verlassen).
		if ( entriesById.Count != seen.Count )
		{
			var toRemove = new List<Guid>();
			foreach ( var id in entriesById.Keys )
			{
				if ( !seen.Contains( id ) ) toRemove.Add( id );
			}
			foreach ( var id in toRemove )
				entriesById.Remove( id );
		}

		// 3. Snapshot der alten Reihenfolge für Change-Detection.
		var oldOrder = new Guid[sorted.Count];
		for ( int i = 0; i < sorted.Count; i++ )
			oldOrder[i] = sorted[i].PlayerId;

		// 4. Neu sortieren.
		sorted.Clear();
		sorted.AddRange( entriesById.Values );
		sorted.Sort( CompareEntries );

		// 5. Position vergeben.
		for ( int i = 0; i < sorted.Count; i++ )
		{
			sorted[i].Position = i + 1;
			var e = sorted[i];
			Log.Info( $"[Rankings] #{e.Position} Player:{e.PlayerId.ToString().Substring( 0, 8 )} | Lap {e.CurrentLap} | CP {e.CheckpointsThisLap} | Finished: {e.RaceFinished} | FinishOrder: {e.FinishOrder} | Time: {e.LastProgressTime:F2}" );
		}

		// 6. Event nur feuern, wenn sich die Reihenfolge wirklich verändert hat.
		bool changed = oldOrder.Length != sorted.Count;
		if ( !changed )
		{
			for ( int i = 0; i < sorted.Count; i++ )
			{
				if ( oldOrder[i] != sorted[i].PlayerId ) { changed = true; break; }
			}
		}
		if ( changed ) OnRankingsUpdated?.Invoke();
	}

	/// <summary>
	/// Sortier-Regel:
	/// 1. Beendete Spieler stehen vor unfertigen.
	/// 2. Unter Beendeten: niedrigere FinishOrder zuerst (1, 2, 3, ...).
	///    FinishOrder == 0 (noch unbestätigt vom Host) wird wie "ganz hinten unter Finishern" behandelt.
	/// 3. Unter Unfertigen: höhere CurrentLap zuerst.
	/// 4. Bei Lap-Gleichstand: höhere CheckpointsThisLap zuerst.
	/// 5. Bei vollem Gleichstand: niedrigere LastProgressTime zuerst (= war früher dort).
	/// </summary>
	private static int CompareEntries( PlayerRankingEntry a, PlayerRankingEntry b )
	{
		if ( a.RaceFinished != b.RaceFinished )
			return a.RaceFinished ? -1 : 1;

		if ( a.RaceFinished && b.RaceFinished )
		{
			int aOrder = a.FinishOrder == 0 ? int.MaxValue : a.FinishOrder;
			int bOrder = b.FinishOrder == 0 ? int.MaxValue : b.FinishOrder;
			return aOrder.CompareTo( bOrder );
		}

		if ( a.CurrentLap != b.CurrentLap )
			return b.CurrentLap.CompareTo( a.CurrentLap );

		if ( a.CheckpointsThisLap != b.CheckpointsThisLap )
			return b.CheckpointsThisLap.CompareTo( a.CheckpointsThisLap );

		// Gleicher Fortschritt: wer früher dort war, ist vorne. Wenn beide noch nie
		// einen Checkpoint hatten (LastProgressTime == 0), ist die Reihenfolge egal.
		if ( a.LastProgressTime == 0f && b.LastProgressTime == 0f )
			return 0;
		if ( a.LastProgressTime == 0f ) return 1;
		if ( b.LastProgressTime == 0f ) return -1;

		return a.LastProgressTime.CompareTo( b.LastProgressTime );
	}

	private PlayerRankingEntry FindOrCreateEntry( PlayerLapTracker tracker )
	{
		var id = ResolvePlayerId( tracker );
		if ( !entriesById.TryGetValue( id, out var entry ) )
		{
			entry = new PlayerRankingEntry
			{
				PlayerId = id,
				Tracker = tracker,
			};
			entriesById[id] = entry;
		}
		else if ( entry.Tracker != tracker )
		{
			entry.Tracker = tracker;
		}
		return entry;
	}

	/// <summary>
	/// Sucht TestControlls.PlayerId am Tracker oder seinen Eltern (analog zu
	/// SectorCheckpoint.FindTracker). Fallback ist die GameObject.Id, damit das
	/// Ranking auch ohne TestControlls-Setup funktioniert.
	/// </summary>
	private static Guid ResolvePlayerId( PlayerLapTracker tracker )
	{
		var current = tracker.GameObject;
		while ( current.IsValid() )
		{
			var ctrl = current.Components.Get<TestControlls>();
			if ( ctrl != null && ctrl.PlayerId != Guid.Empty )
				return ctrl.PlayerId;
			current = current.Parent;
		}
		return tracker.GameObject.Id;
	}
}
