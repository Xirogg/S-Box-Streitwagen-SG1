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

	private readonly List<ChariotPhysics> buffed = new();
	private readonly List<ChariotPhysics> debuffed = new();
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

		// Stay visible exactly as long as the shield itself is up — it can be
		// consumed early by an incoming reflect, in which case the HUD should
		// drop the line the moment the shield flips inactive.
		var shieldRef = shield;
		ResolveNotifier()?.ShowDynamic(
			"Shield active",
			() => shieldRef.IsValid() && shieldRef.IsActive ? 1f : 0f,
			withCountdown: false );
	}

	// ---------- Ult ----------

	protected override bool CanActivateUltimate() => !effectActive;

	protected override void OnActivateUltimate()
	{
		effectActive = true;
		buffed.Clear();
		debuffed.Clear();
		ResolveNotifier()?.ShowTimed( "Kaaarma", EffectDuration );
		// Sound A + voice, worldwide.
		GodPowersUltimateSfxmodule.Instance?.PlayMaatUltimate();

		var players = new List<GameObject>( Scene.FindAllWithTag( PlayerTag ) );
		if ( players.Count == 0 ) return;

		// Zufällig in zwei Hälften aufteilen — Caster ist mit drin.
		Shuffle( players );
		int half = (players.Count + 1) / 2; // bei ungerader Zahl bekommt der Buff einen mehr

		var ownerChariot = Owner.IsValid()
			? Owner.Components.Get<ChariotPhysics>( FindMode.EverythingInSelfAndDescendants )
			: null;

		for ( int i = 0; i < players.Count; i++ )
		{
			var player = players[i];
			var chariot = player.Components.Get<ChariotPhysics>( FindMode.EverythingInSelfAndDescendants );
			if ( chariot is null )
			{
				Log.Warning( $"[MaatPower] Spieler '{player.Name}' hat keine ChariotPhysics — übersprungen." );
				continue;
			}

			bool wantsBuff = i < half;

			// Karma-Schild: Debuff-Ziele dürfen reflektieren.
			if ( !wantsBuff )
			{
				var shield = player.Components.Get<MaatKarmaShield>( FindMode.EverythingInSelfAndDescendants );
				if ( shield is not null && shield.TryConsume() )
				{
					ApplyBuff( chariot );
					if ( ownerChariot is not null && ownerChariot != chariot )
						ApplyDebuff( ownerChariot );
					continue;
				}
			}

			if ( wantsBuff )
				ApplyBuff( chariot );
			else
				ApplyDebuff( chariot );
		}

		//Log.Info( $"[MaatPower] Buff: {buffed.Count}, Debuff: {debuffed.Count} für {EffectDuration}s" );

		Invoke( EffectDuration, RevertEffect );
	}

	private void ApplyBuff( ChariotPhysics chariot )
	{
		chariot.SetSpeedMultiplier( BuffKey, BuffMultiplier );
		if ( !buffed.Contains( chariot ) )
			buffed.Add( chariot );
	}

	private void ApplyDebuff( ChariotPhysics chariot )
	{
		chariot.SetSpeedMultiplier( DebuffKey, DebuffMultiplier );
		if ( !debuffed.Contains( chariot ) )
			debuffed.Add( chariot );
	}

	private void RevertEffect()
	{
		foreach ( var c in buffed )
		{
			if ( c.IsValid() ) c.ClearSpeedMultiplier( BuffKey );
		}
		foreach ( var c in debuffed )
		{
			if ( c.IsValid() ) c.ClearSpeedMultiplier( DebuffKey );
		}

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
