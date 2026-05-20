using Sandbox;
using System;

public sealed class PlayerDamageSystem : Component, Component.ICollisionListener
{
	[Property, Group( "Health" )] public float MaxHP { get; set; } = 100f;

	[Property, Group( "Damage" )] public float HitCooldown { get; set; } = 0.5f;

	// Hook for later (powerups, difficulty). Leave at 1.0 for now.
	[Property, Group( "Damage" )] public float DamageMultiplier { get; set; } = 1.0f;

	[Property, Group( "Speed Penalty" ), Range( 0f, 1f )]
	public float MinSpeedMultiplier { get; set; } = 0.6f;

	[Property, Group( "Refs" )] public PlayerStats Stats { get; set; }

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = true;

	[RequireComponent] public Rigidbody Body { get; set; }

	private const string SpeedKey = "damage";

	public float CurrentHP { get; private set; }
	public bool IsTotaled => CurrentHP <= 0f;

	private float _lastHitTime = float.NegativeInfinity;

	protected override void OnStart()
	{
		Stats ??= Components.Get<PlayerStats>( FindMode.EverythingInSelfAndDescendants )
				?? GameObject.Root?.Components.Get<PlayerStats>( FindMode.EverythingInSelfAndDescendants );

		CurrentHP = MaxHP;
		ApplySpeedFromHP();
	}

	public void OnCollisionStart( Collision o )
	{
		if ( IsTotaled ) return;
		if ( Time.Now - _lastHitTime < HitCooldown ) return;

		var otherRoot = o.Other.GameObject?.Root;
		if ( otherRoot == GameObject.Root ) return;
		if ( o.Other.GameObject?.Tags.Has( "ground" ) == true ) return;

		var attackerStats = otherRoot?.Components.Get<PlayerStats>( FindMode.EverythingInSelfAndDescendants );
		if ( attackerStats is null || Stats is null ) return;

		float damage = Stats.ComputeIncomingDamage( attackerStats ) * DamageMultiplier;
		if ( damage <= 0f ) return;

		_lastHitTime = Time.Now;
		CurrentHP = MathF.Max( 0f, CurrentHP - damage );
		ApplySpeedFromHP();

		if ( DebugLog )
			Log.Info( $"[Damage] hit by {otherRoot?.Name ?? "?"} | dmg={damage:F1} hp={CurrentHP:F1}/{MaxHP:F0}" );
	}

	public void OnCollisionUpdate( Collision o ) { }
	public void OnCollisionStop( CollisionStop o ) { }

	private void ApplySpeedFromHP()
	{
		float multiplier;
		if ( CurrentHP <= 0f )
		{
			multiplier = 0f;
		}
		else
		{
			float frac = MathX.Clamp( (CurrentHP - 1f) / MathF.Max( MaxHP - 1f, 0.0001f ), 0f, 1f );
			multiplier = MathX.Lerp( MinSpeedMultiplier, 1f, frac );
		}

		var target = Components.Get<ISpeedModifiable>( FindMode.EverythingInSelfAndDescendants );
		target?.SetSpeedMultiplier( SpeedKey, multiplier );
	}

	public void Damage( float amount )
	{
		if ( amount <= 0f ) return;
		CurrentHP = MathF.Max( 0f, CurrentHP - amount );
		ApplySpeedFromHP();
	}

	public void Heal( float amount )
	{
		if ( amount <= 0f ) return;
		CurrentHP = MathF.Min( MaxHP, CurrentHP + amount );
		ApplySpeedFromHP();
	}

	public void ResetHP()
	{
		CurrentHP = MaxHP;
		_lastHitTime = float.NegativeInfinity;
		ApplySpeedFromHP();
	}
}
