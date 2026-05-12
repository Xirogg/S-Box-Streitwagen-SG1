using System;
using System.Collections.Generic;
using Sandbox;

namespace LapSystem;

/// <summary>
/// Liegt auf jedem Spieler-GameObject. Trackt Rundenzahl und besuchte Sektoren
/// fuer genau diesen Spieler. Owner-autoritativ: nur der besitzende Client
/// schreibt den State, alle anderen erhalten die Werte ueber [Sync].
/// </summary>
public sealed class PlayerLapTracker : Component
{

	public float RetriggerCooldown { get; set; } = 1.5f;

	// Replizierter State – auf allen Clients sichtbar (UI, HUD usw.)
	[Sync] public int CurrentLap { get; set; }
	[Sync] public bool RaceFinished { get; set; }

	// Fortschritt innerhalb der aktuellen Runde – wird vom RaceRankingManager
	// fuers Live-Ranking gelesen.
	[Sync] public int CheckpointsThisLap { get; set; }
	[Sync] public float LastProgressTime { get; set; }

	public int MaxLaps => RaceManager.Instance?.MaxLaps ?? 3;
	public bool RaceActive => !RaceFinished;

	// Lokale C#-Events – feuern auf jedem Client (per RPC verteilt)
	public event Action<int> OnLapCompleted;
	public event Action OnFinished;
	public event Action OnInvalidLap;

	// Owner-only state (wird nicht synchronisiert, lebt nur auf der Owner-Seite)
	private readonly HashSet<int> passedSectors = new();
	private float lastLineCrossTime = -999f;

	protected override void OnStart()
	{
		if ( Network.IsOwner )
			ResetForNewRace();
	}

	public void ResetForNewRace()
	{
		if ( !Network.IsOwner ) return;

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
		if ( !Network.IsOwner ) return;
		if ( RaceFinished ) return;
		if ( CurrentLap == 0 ) return; // erst nach erstem Linien-Crossing zaehlen

		if ( !passedSectors.Add( sector.SectorIndex ) ) return; // schon gehabt

		CheckpointsThisLap = passedSectors.Count;
		LastProgressTime = Time.Now;
	}

	/// <summary> Wird von der StartFinishLine aufgerufen, wenn DIESER Spieler sie kreuzt. </summary>
	public void HandleStartLineCrossed()
	{
		if ( !Network.IsOwner ) return;
		if ( RaceFinished ) return;

		// Per-Player Cooldown gegen Mehrfach-Trigger durch viele Colliders
		if ( Time.Now - lastLineCrossTime < RetriggerCooldown ) return;
		lastLineCrossTime = Time.Now;

		var rm = RaceManager.Instance;
		if ( rm == null ) return;

		// Erste Ueberquerung -> Runde 1 startet
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
				RpcNotifyHostFinished();
				return;
			}

			CheckpointsThisLap = 0;
			passedSectors.Clear();
		}
		else
		{
			// Sektor uebersprungen -> Runde ungueltig (z. B. Abkuerzung)
			RpcInvalidLap();
		}
	}

	[Rpc.Broadcast]
	private void RpcLapCompleted( int lap ) => OnLapCompleted?.Invoke( lap );

	[Rpc.Broadcast]
	private void RpcFinished() => OnFinished?.Invoke();

	[Rpc.Broadcast]
	private void RpcInvalidLap() => OnInvalidLap?.Invoke();

	/// <summary>
	/// Teilt dem Host mit, dass dieser Spieler ins Ziel gekommen ist,
	/// damit der RaceManager den FinishOrder vergeben kann.
	/// </summary>
	[Rpc.Host]
	private void RpcNotifyHostFinished()
	{
		RaceManager.Instance?.NotifyPlayerFinished( this );
	}
}
