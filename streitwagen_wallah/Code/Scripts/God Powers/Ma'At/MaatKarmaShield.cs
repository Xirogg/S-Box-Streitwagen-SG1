using Sandbox;
using System;

/// <summary>
/// Passiver Karma-Schild von Ma'at. Wird vom MaatPower aktiviert und liegt
/// auf dem Spieler-Prefab. Andere Powers fragen via TryConsume() ab, ob sie
/// reflektiert werden müssen. Trauben-Projektile werden direkt hier per
/// Kollision behandelt (Velocity invertieren — kein Auto-Aim).
/// </summary>
public sealed class MaatKarmaShield : Component, Component.ICollisionListener
{
	[Property, Group( "Reflection" )]
	public string GrapeTag { get; set; } = "grape";

	public bool IsActive { get; private set; }

	public event Action OnConsumed;

	private float _expiresAt;

	public void Activate( float duration )
	{
		IsActive = true;
		_expiresAt = Time.Now + duration;
		PlayShieldSound(); // Sound A — shield raised (proximity)
	}

	/// <summary>True und konsumiert den Schild, wenn aktiv. Sonst false.</summary>
	public bool TryConsume()
	{
		if ( !IsActive ) return false;
		IsActive = false;
		PlayShieldSound(); // Sound A — shield destroyed/reflected (proximity)
		OnConsumed?.Invoke();
		return true;
	}

	/// <summary>
	/// Route Ma'at's normal sound through the player's persistent SFX module. The shield
	/// itself isn't erased like the GodPower clone, so it can safely own this call.
	/// </summary>
	private void PlayShieldSound()
	{
		var root = GameObject?.Root;
		var sfx = root?.Components.Get<GodPowersNormalSfxmodule>( FindMode.EverythingInSelfAndDescendants );
		sfx?.PlayMaatShield();
	}

	protected override void OnUpdate()
	{
		if ( IsActive && Time.Now >= _expiresAt )
			IsActive = false;
	}

	public void OnCollisionStart( Collision o )
	{
		if ( !IsActive ) return;

		var otherRoot = o.Other.GameObject?.Root;
		if ( otherRoot is null || otherRoot == GameObject.Root ) return;
		if ( !otherRoot.Tags.Has( GrapeTag ) ) return;

		var rb = o.Other.GameObject.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		if ( rb is not null )
			rb.Velocity = -rb.Velocity;

		TryConsume();
	}

	public void OnCollisionUpdate( Collision o ) { }
	public void OnCollisionStop( CollisionStop o ) { }
}
