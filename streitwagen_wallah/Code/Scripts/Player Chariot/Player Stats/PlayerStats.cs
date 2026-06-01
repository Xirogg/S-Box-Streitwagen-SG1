using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>
/// Central Stat Class and Logic for each Player 
/// A instance of this Script should be on each Player on the specific Node 
/// Regulates all Player Stats as well as Buffs, Debuffs and or Modifiers
/// </summary>
public sealed class PlayerStats : Component
{
	[Property, Group( "Refs" )] public ChariotPhysics Chariot { get; set; }

	/// <summary>
	/// Percent-based ram weight. 100 = normal force, 110 = 10% more, 90 = 10% less.
	/// Read by PlayerCollisions to scale ram impulses; no longer touches Rigidbody mass.
	/// </summary>
	[Property, Group( "Weight" )] public Stat Weight { get; set; } = new() { BaseValue = 100f };
	[Property, Group( "MaxSpeed" )] public Stat MaxSpeedPercent { get; set; } = new() { BaseValue = 100f };
	[Property, Group( "Attack" )] public Stat Attack { get; set; } = new() { BaseValue = 30f };
	[Property, Group( "Defense" )] public Stat Defense { get; set; } = new() { BaseValue = 100f };

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = false;

	public const float WeightBaseline = 100f;
	private const float SpeedBaseline = 100f;
	private const string SpeedKey = "stats";

	private bool _initialized;

	/// <summary>
	/// Weight as a 0..n multiplier (100 -> 1.0, 110 -> 1.1, 90 -> 0.9). Clamped to >=0
	/// so a debuff can't invert direction.
	/// </summary>
	public float WeightMultiplier => MathF.Max( Weight.Value, 0f ) / WeightBaseline;

	protected override void OnStart()
	{
		_initialized = true;
		ApplyAll();
	}

	protected override void OnFixedUpdate()
	{
		if ( !_initialized ) return;
		ApplyAll();
	}

	private void ApplyAll()
	{
		ApplyMaxSpeed();
	}

	private void ApplyMaxSpeed()
	{
		if ( Chariot is null ) return;
		float multiplier = MaxSpeedPercent.Value / SpeedBaseline;
		Chariot.SetSpeedMultiplier( SpeedKey, multiplier );
	}

	/// <summary>
	/// League-style damage: incoming = AD * 100 / (100 + Defense).
	/// 0 def -> full damage, 100 def -> 50%, 200 def -> 33%, ...
	/// </summary>
	public float ComputeIncomingDamage( PlayerStats attacker )
	{
		if ( attacker is null ) return 0f;
		float ad = MathF.Max( attacker.Attack.Value, 0f );
		float def = MathF.Max( Defense.Value, 0f );
		float multiplier = 100f / (100f + def);
		float dmg = ad * multiplier;

		if ( DebugLog )
			Log.Info( $"[Stats] {attacker.GameObject.Root?.Name} AD={ad:F1} vs {GameObject.Root?.Name} DEF={def:F1} -> {dmg:F1} dmg ({multiplier * 100f:F0}%)" );

		return dmg;
	}

	/// <summary>
	/// A single stat. BaseValue is what's edited in the prefab; modifiers are
	/// runtime-only and stack additively by key so multiple sources can buff/debuff
	/// the same stat without overwriting each other.
	/// </summary>
	public class Stat
	{
		[Property] public float BaseValue { get; set; } = 100f;

		private readonly Dictionary<string, float> _flatMods = new();

		public float Value
		{
			get
			{
				float v = BaseValue;
				foreach ( var m in _flatMods.Values )
					v += m;
				return v;
			}
		}

		public void SetModifier( string key, float flat ) => _flatMods[key] = flat;
		public void ClearModifier( string key ) => _flatMods.Remove( key );
		public void ClearAllModifiers() => _flatMods.Clear();

		public static implicit operator float( Stat s ) => s?.Value ?? 0f;
	}
}
