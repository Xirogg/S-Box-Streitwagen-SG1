using Sandbox;

public sealed class DionysosGrapesPrefabs : Component, Component.ICollisionListener
{
	[Property] public float Lifetime { get; set; } = 10f;
	[Property] public float BounceMultiplier { get; set; } = 1f;
	[Property] public string GrapeTag { get; set; } = "Grape";

	/// <summary>Schaden, den eine Traube beim Treffer auf einen Spieler austeilt.</summary>
	[Property] public float Damage { get; set; } = 20f;

	/// <summary>
	/// Scharfschalt-Zeitfenster: für so viele Sekunden nach dem Spawn ist der
	/// Collider aus und es wird kein Schaden ausgeteilt, damit der Caster sich
	/// nicht sofort selbst trifft.
	/// </summary>
	[Property] public float ArmDelay { get; set; } = 0.5f;

	private Rigidbody rb;
	private Collider collider;
	private float spawnTime;

	protected override void OnStart()
	{
		rb = Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		collider = Components.Get<Collider>( FindMode.EverythingInSelfAndDescendants );
		spawnTime = Time.Now;

		// Collider während des Scharfschalt-Fensters ausschalten.
		if ( collider is not null )
			collider.Enabled = false;
	}

	protected override void OnUpdate()
	{
		if ( Time.Now - spawnTime >= Lifetime )
		{
			GameObject.Destroy();
			return;
		}

		// Nach Ablauf von ArmDelay den Collider (wieder) aktivieren.
		if ( collider is not null && !collider.Enabled && IsArmed )
			collider.Enabled = true;
	}

	/// <summary>True sobald das Scharfschalt-Fenster abgelaufen ist.</summary>
	private bool IsArmed => Time.Now - spawnTime >= ArmDelay;

	public void OnCollisionStart( Collision o )
	{
		if ( o.Other.GameObject.Tags.Has( GrapeTag ) ) return;

		// Während des Scharfschalt-Fensters ignorieren wir jede Kollision komplett.
		if ( !IsArmed ) return;

		// Spieler-Treffer (inkl. Caster): Schaden austeilen und selbst despawnen.
		// Der Collider hängt an einem Kind (Wagen/Pferd); das PlayerDamageSystem sitzt
		// an der Root. Deshalb NICHT über ein Tag am direkten Collider-Objekt gehen
		// (das trägt es meist nicht), sondern über die Root nach dem DamageSystem
		// suchen — genau so, wie PlayerDamageSystem selbst den Angreifer auflöst.
		var dmg = o.Other.GameObject.Root?.Components.Get<PlayerDamageSystem>( FindMode.EverythingInSelfAndDescendants );
		if ( dmg is not null )
		{
			dmg.Damage( Damage );

			GameObject.Destroy();
			return;
		}

		// Nicht-Spieler (Wände/Boden): weiterhin abprallen.
		if ( rb is null ) return;
		var normal = o.Contact.Normal;
		var reflected = Vector3.Reflect( rb.Velocity, normal );
		rb.Velocity = reflected * BounceMultiplier;
	}

	public void OnCollisionUpdate( Collision o ) { }
	public void OnCollisionStop( CollisionStop o ) { }
}
