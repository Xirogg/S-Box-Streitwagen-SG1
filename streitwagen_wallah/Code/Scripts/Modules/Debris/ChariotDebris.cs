using Sandbox;

namespace Sandbox;

public sealed class ChariotDebris : Component, Component.ITriggerListener
{
	[Property, Group( "Debris" )] public float SpawnTriggerCooldown { get; set; } = 2f;
	[Property, Group( "Debris" )] public float DamageAmount { get; set; } = 10f;
	[Property, Group( "Debris" )] public string PlayerTag { get; set; } = "player";

	private float _spawnTime;
	private bool _destroyed;

	protected override void OnAwake()
	{
		var col = Components.Get<BoxCollider>();
		if ( col != null ) col.IsTrigger = true;
	}

	protected override void OnStart()
	{
		_spawnTime = Time.Now;
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( _destroyed ) return;
		if ( Time.Now - _spawnTime < SpawnTriggerCooldown ) return;
		if ( !other.GameObject.IsValid() ) return;

		if ( !string.IsNullOrEmpty( PlayerTag ) && !other.GameObject.Root.Tags.Has( PlayerTag ) )
			return;

		var damageSystem = FindDamageSystem( other.GameObject );
		if ( damageSystem == null ) return;

		damageSystem.Damage( DamageAmount );

		_destroyed = true;
		GameObject.Destroy();
	}

	public void OnTriggerExit( Collider other ) { }

	private static PlayerDamageSystem FindDamageSystem( GameObject go )
	{
		var current = go;
		while ( current.IsValid() )
		{
			var s = current.Components.Get<PlayerDamageSystem>();
			if ( s != null ) return s;
			current = current.Parent;
		}
		return null;
	}
}
