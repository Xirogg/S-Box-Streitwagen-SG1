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

	// Zieleinlauf-Reihenfolge: 1, 2, 3, ... in der Reihenfolge des Ankommens.
	// 0 = noch nicht im Ziel. Wird HOST-autoritativ vom RaceRankingManager vergeben
	// und via SyncFlags.FromHost an alle Clients repliziert.
	//
	// FromHost ist zwingend, weil dieser Tracker sonst OWNER-autoritativ ist: jeder
	// Spieler wird per NetworkSpawn(channel) dem eigenen Client zugewiesen, der Host
	// besitzt fremde Tracker also NICHT und könnte ihren Wert ohne dieses Flag gar
	// nicht setzen. Nur der Host schreibt FinishOrder, alle anderen lesen ihn.
	[Sync( SyncFlags.FromHost )] public int FinishOrder { get; set; }

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
		if ( CurrentLap == 0 ) return; // Lap 0 ist nur der Grid-Start, zaehlt noch nicht

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

		// Lap 0 ist nur der Start am Grid (Startlinie liegt direkt vor den Spawnpoints) ->
		// keine Sektoren noetig, direkt weiter zu Lap 1.
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
			LastProgressTime = Time.Now;
			RpcLapCompleted( CurrentLap );

			// CurrentLap zaehlt abgeschlossene Runden -> MaxLaps erreicht = Rennen vorbei
			if ( CurrentLap >= rm.MaxLaps )
			{
				RaceFinished = true;
				RpcFinished();
				RpcNotifyHostFinished();
				return;
			}

			CurrentLap++;
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
