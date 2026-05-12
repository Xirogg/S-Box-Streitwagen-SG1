using Sandbox;

namespace LapSystem;

/// <summary>
/// Start/Ziel-Linie. Sobald ein Spieler durchfährt, wird die HandleStartLineCrossed()-Methode
/// SEINES PlayerLapTracker aufgerufen. Der Per-Player-Cooldown gegen Mehrfach-Trigger
/// liegt im PlayerLapTracker selbst (sonst würde Spieler A's Cooldown Spieler B blockieren).
/// </summary>
public sealed class StartFinishLine : Component, Component.ITriggerListener
{
	protected override void OnAwake()
	{
		
		var col = Components.Get<BoxCollider>();
		Log.Info("Yallawwh" + col );
		if ( col != null ) col.IsTrigger = true;
	}

	public void OnTriggerEnter( Collider other )
	{
		var tracker = FindTracker( other.GameObject );
		if ( tracker == null ) return;

		tracker.HandleStartLineCrossed();
	}

	public void OnTriggerExit( Collider other ) { }

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
