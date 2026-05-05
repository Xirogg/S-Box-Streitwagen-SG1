using System;
using System.Collections.Generic;
using Sandbox;

namespace LapSystem;

/// <summary>
/// Liegt auf jedem Spieler-GameObject. Trackt Rundenzahl und besuchte Sektoren
/// für genau diesen Spieler. Host-autoritativ: nur der Host schreibt den State,
/// Clients erhalten die Werte über [Sync].
/// </summary>
public sealed class PlayerLapTracker : Component
{
	
	public float RetriggerCooldown { get; set; } = 1.5f;

	// Replizierter State – auf allen Clients sichtbar (UI, HUD usw.)
	[Sync] public int CurrentLap { get; set; }
	[Sync] public bool RaceFinished { get; set; }

	// Fortschritt innerhalb der aktuellen Runde – wird vom RaceRankingManager
	// fürs Live-Ranking gelesen.
	[Sync] public int CheckpointsThisLap { get; set; }
	[Sync] public float LastProgressTime { get; set; }

	public int MaxLaps => RaceManager.Instance?.MaxLaps ?? 3;
	public bool RaceActive => !RaceFinished;

	// Lokale C#-Events – feuern auf jedem Client (per RPC verteilt)
	public event Action<int> OnLapCompleted;
	public event Action OnFinished;
	public event Action OnInvalidLap;

	// Host-only state (wird nicht synchronisiert, lebt nur auf der Host-Seite)
	private readonly HashSet<int> passedSectors = new();
	private float lastLineCrossTime = -999f;

	protected override void OnStart()
	{
		if ( Networking.IsHost )
			ResetForNewRace();
	}

	public void ResetForNewRace()
	{
		if ( !Networking.IsHost ) return;

		CurrentLap = 0;
		RaceFinished = false;
		CheckpointsThisLap = 0;
		LastProgressTime = 0f;
		passedSectors.Clear();
		lastLineCrossTime = -999f;
	}

	/// <summary> Wird vom SectorCheckpoint aufgerufen, wenn DIESER Spieler ihn passiert. </summary>
	public void HandleSectorPassed( SectorCheckpoint sector )
	{
		if ( !Networking.IsHost ) return;
		if ( RaceFinished ) return;
		if ( CurrentLap == 0 ) return; // erst nach erstem Linien-Crossing zählen

		if ( !passedSectors.Add( sector.SectorIndex ) ) return; // schon gehabt

		CheckpointsThisLap = passedSectors.Count;
		LastProgressTime = Time.Now;
	}

	/// <summary> Wird von der StartFinishLine aufgerufen, wenn DIESER Spieler sie kreuzt. </summary>
	public void HandleStartLineCrossed()
	{
		if ( !Networking.IsHost ) return;
		if ( RaceFinished ) return;

		// Per-Player Cooldown gegen Mehrfach-Trigger durch viele Colliders
		if ( Time.Now - lastLineCrossTime < RetriggerCooldown ) return;
		lastLineCrossTime = Time.Now;

		var rm = RaceManager.Instance;
		if ( rm == null ) return;

		// Erste Überquerung -> Runde 1 startet
		if ( CurrentLap == 0 )
		{
			CurrentLap = 1;
			CheckpointsThisLap = 0;
			LastProgressTime = Time.Now;
			passedSectors.Clear();
			return;
		}

		// Alle Sektoren in dieser Runde besucht? -> Runde gilt
		if ( passedSectors.Count >= rm.Sectors.Count )
		{
			CurrentLap++;
			LastProgressTime = Time.Now;
			RpcLapCompleted( CurrentLap - 1 );

			if ( CurrentLap > rm.MaxLaps )
			{
				RaceFinished = true;
				RpcFinished();
				rm.NotifyPlayerFinished( this );
				return;
			}

			CheckpointsThisLap = 0;
			passedSectors.Clear();
		}
		else
		{
			// Sektor übersprungen -> Runde ungültig (z. B. Abkürzung)
			RpcInvalidLap();
		}
	}

	[Rpc.Broadcast]
	private void RpcLapCompleted( int lap ) => OnLapCompleted?.Invoke( lap );

	[Rpc.Broadcast]
	private void RpcFinished() => OnFinished?.Invoke();

	[Rpc.Broadcast]
	private void RpcInvalidLap() => OnInvalidLap?.Invoke();
}
