using Sandbox;

public sealed class DionysosGrapesPrefabs : Component, Component.ICollisionListener
{
	[Property] public float Lifetime { get; set; } = 10f;
	[Property] public float BounceMultiplier { get; set; } = 1f;
	[Property] public string GrapeTag { get; set; } = "Grape";

	private Rigidbody rb;
	private float spawnTime;

	protected override void OnStart()
	{
		rb = Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		spawnTime = Time.Now;
	}

	protected override void OnUpdate()
	{
		if ( Time.Now - spawnTime >= Lifetime )
			GameObject.Destroy();
	}

	public void OnCollisionStart( Collision o )
	{
		if ( o.Other.GameObject.Tags.Has( GrapeTag ) ) return;

		if ( rb is null ) return;
		var normal = o.Contact.Normal;
		var reflected = Vector3.Reflect( rb.Velocity, normal );
		rb.Velocity = reflected * BounceMultiplier;
	}

	public void OnCollisionUpdate( Collision o ) { }
	public void OnCollisionStop( CollisionStop o ) { }
}
