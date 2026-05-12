using Sandbox;

namespace LapSystem;

/// <summary>
/// Trigger-Volumen im Track. Im Editor mit einem BoxCollider (oder anderen
/// Collider, IsTrigger = true) platzieren. Hat selbst keinen State – fragt
/// beim Trigger-Enter den PlayerLapTracker des eintretenden Spielers ab.
/// </summary>
public sealed class SectorCheckpoint : Component, Component.ITriggerListener
{
	[Property] public int SectorIndex { get; set; }

	protected override void OnAwake()
	{
		// BoxCollider standardmäßig auf Trigger schalten, damit man es im Editor nicht vergisst
		var col = Components.Get<BoxCollider>();
		if ( col != null ) col.IsTrigger = true;
	}

	public void OnTriggerEnter( Collider other )
	{
		var tracker = FindTracker( other.GameObject );
		if ( tracker == null ) return;

		tracker.HandleSectorPassed( this );
	}

	public void OnTriggerExit( Collider other ) { }

	/// <summary>
	/// Sucht den PlayerLapTracker am eintretenden GameObject oder an einem seiner Eltern.
	/// Wichtig, weil der Collider oft an einem Kind-Objekt sitzt (z. B. Body) und der
	/// Tracker am Spieler-Root.
	/// </summary>
	private static PlayerLapTracker FindTracker( GameObject go )
	{
		var current = go;
		while ( current.IsValid() )
		{
			var t = current.Components.Get<PlayerLapTracker>();
			if ( t != null ) return t;
			current = current.Parent;
		}
		return null;
	}
}
