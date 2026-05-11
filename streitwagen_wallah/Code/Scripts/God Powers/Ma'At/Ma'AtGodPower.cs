using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>
/// Ma'at:
///   Normal — Karma-Schild: aktiviert den passiven MaatKarmaShield am Spieler.
///   Der Schild reflektiert eine eingehende Aktion (Dionysos-Traube,
///   Laverna-Hehler, Ma'at-Ult-Debuff) und verbraucht sich dabei.
///
///   Ultimate — Das jüngste Gericht: alle Spieler werden zufällig 50/50 in
///   Buff/Debuff aufgeteilt (kein Ranking-System vorhanden). Geschützte
///   Spieler bekommen statt des Debuffs den Buff — der Caster zahlt mit dem
///   Debuff drauf.
/// </summary>
public sealed class MaatPower : GodPower
{
	[Property, Group( "Karma Shield" )]
	public float ShieldDuration { get; set; } = 8f;

	[Property, Group( "Judgement (Ult)" )]
	public string PlayerTag { get; set; } = "player";

	[Property, Group( "Judgement (Ult)" )]
	public float BuffMultiplier { get; set; } = 1.5f;

	[Property, Group( "Judgement (Ult)" )]
	public float DebuffMultiplier { get; set; } = 0.6f;

	[Property, Group( "Judgement (Ult)" )]
	public float EffectDuration { get; set; } = 6f;

	private const string BuffKey = "maat_buff";
	private const string DebuffKey = "maat_debuff";

	private readonly List<ISpeedModifiable> buffed = new();
	private readonly List<ISpeedModifiable> debuffed = new();
	private bool effectActive;

	// ---------- Normal ----------

	protected override void OnActivate()
	{
		if ( !Owner.IsValid() ) return;

		var shield = Owner.Components.Get<MaatKarmaShield>( FindMode.EverythingInSelfAndDescendants );
		if ( shield is null )
		{
			Log.Warning( "[MaatPower] Owner hat keinen MaatKarmaShield." );
			return;
		}

		shield.Activate( ShieldDuration );
	}

	// ---------- Ult ----------

	protected override bool CanActivateUltimate() => !effectActive;

	protected override void OnActivateUltimate()
	{
		effectActive = true;
		buffed.Clear();
		debuffed.Clear();

		var players = new List<GameObject>( Scene.FindAllWithTag( PlayerTag ) );
		if ( players.Count == 0 ) return;

		// Zufällig in zwei Hälften aufteilen — Caster ist mit drin.
		Shuffle( players );
		int half = (players.Count + 1) / 2; // bei ungerader Zahl bekommt der Buff einen mehr

		var ownerSpeed = Owner.IsValid()
			? Owner.Components.Get<ISpeedModifiable>( FindMode.EverythingInSelfAndDescendants )
			: null;

		for ( int i = 0; i < players.Count; i++ )
		{
			var player = players[i];
			var speed = player.Components.Get<ISpeedModifiable>( FindMode.EverythingInSelfAndDescendants );
			if ( speed is null ) continue;

			bool wantsBuff = i < half;

			// Karma-Schild: Debuff-Ziele dürfen reflektieren.
			if ( !wantsBuff )
			{
				var shield = player.Components.Get<MaatKarmaShield>( FindMode.EverythingInSelfAndDescendants );
				if ( shield is not null && shield.TryConsume() )
				{
					ApplyBuff( speed );
					if ( ownerSpeed is not null && ownerSpeed != speed )
						ApplyDebuff( ownerSpeed );
					continue;
				}
			}

			if ( wantsBuff )
				ApplyBuff( speed );
			else
				ApplyDebuff( speed );
		}

		Log.Info( $"[MaatPower] Buff: {buffed.Count}, Debuff: {debuffed.Count} für {EffectDuration}s" );

		Invoke( EffectDuration, RevertEffect );
	}

	private void ApplyBuff( ISpeedModifiable speed )
	{
		speed.SetSpeedMultiplier( BuffKey, BuffMultiplier );
		if ( !buffed.Contains( speed ) )
			buffed.Add( speed );
	}

	private void ApplyDebuff( ISpeedModifiable speed )
	{
		speed.SetSpeedMultiplier( DebuffKey, DebuffMultiplier );
		if ( !debuffed.Contains( speed ) )
			debuffed.Add( speed );
	}

	private void RevertEffect()
	{
		foreach ( var s in buffed )
			s?.ClearSpeedMultiplier( BuffKey );
		foreach ( var s in debuffed )
			s?.ClearSpeedMultiplier( DebuffKey );

		buffed.Clear();
		debuffed.Clear();
		effectActive = false;
	}

	protected override void OnDisabled()
	{
		if ( effectActive )
			RevertEffect();
	}

	private static void Shuffle<T>( List<T> list )
	{
		for ( int i = list.Count - 1; i > 0; i-- )
		{
			int j = Random.Shared.Int( 0, i );
			(list[i], list[j]) = (list[j], list[i]);
		}
	}
}
