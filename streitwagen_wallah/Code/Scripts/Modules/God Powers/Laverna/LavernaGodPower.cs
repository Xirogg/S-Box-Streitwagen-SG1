using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>
/// Laverna:
///   Ultimate — Göttlicher Hehler: Laverna stiehlt von allen Mitspielern, die
///   mehr HP haben als der Caster, einen gleichen HP-Anteil — bis der Caster
///   wieder volle HP hat. Aufgerundet pro Spender, daher kann ein bisschen HP
///   "verloren" gehen (z.B. 5 HP fehlen, 3 reichere Spieler → 2 HP von jedem
///   = 6 gestohlen, Caster heilt nur 5).
///
///   Normal (Item-Dieb): noch nicht implementiert — Item-System fehlt.
/// </summary>
public sealed class LavernaPower : GodPower
{
	[Property, Group( "Heist" )]
	public string PlayerTag { get; set; } = "player";

	protected override void OnActivate()
	{
		// Item-Dieb noch nicht eingebaut.
	}

	protected override void OnActivateUltimate()
	{
		if ( !Owner.IsValid() )
		{
			Log.Warning( "[LavernaPower] Kein Owner zugewiesen." );
			return;
		}

		var ownerDamage = Owner.Components.Get<PlayerDamageSystem>( FindMode.EverythingInSelfAndDescendants );
		if ( ownerDamage is null )
		{
			Log.Warning( "[LavernaPower] Owner hat kein PlayerDamageSystem." );
			return;
		}

		float casterHP = ownerDamage.CurrentHP;
		float deficit = ownerDamage.MaxHP - casterHP;
		if ( deficit <= 0f )
		{
			Log.Info( "[LavernaPower] Caster ist bereits voll — nichts zu stehlen." );
			return;
		}

		var donors = new List<PlayerDamageSystem>();
		foreach ( var player in Scene.FindAllWithTag( PlayerTag ) )
		{
			var dmg = player.Components.Get<PlayerDamageSystem>( FindMode.EverythingInSelfAndDescendants );
			if ( dmg is null || dmg == ownerDamage ) continue;
			if ( dmg.CurrentHP > casterHP )
				donors.Add( dmg );
		}

		if ( donors.Count == 0 )
		{
			Log.Info( "[LavernaPower] Kein reicherer Mitspieler gefunden." );
			return;
		}

		// Karma-Schild: geschützte Spender heilen sich voll, Caster zahlt mit HP.
		for ( int i = donors.Count - 1; i >= 0; i-- )
		{
			var donor = donors[i];
			var shield = donor.GameObject.Components.Get<MaatKarmaShield>( FindMode.EverythingInSelfAndDescendants );
			if ( shield is null || !shield.TryConsume() ) continue;

			float refund = donor.MaxHP - donor.CurrentHP;
			if ( refund > 0f )
			{
				donor.Heal( refund );
				ownerDamage.Damage( refund );
			}
			donors.RemoveAt( i );
		}

		if ( donors.Count == 0 )
		{
			Log.Info( "[LavernaPower] Alle Spender hatten Karma-Schild." );
			return;
		}

		float perPlayer = MathF.Ceiling( deficit / donors.Count );
		float totalStolen = 0f;
		foreach ( var donor in donors )
		{
			float take = MathF.Min( perPlayer, donor.CurrentHP );
			if ( take <= 0f ) continue;
			donor.Damage( take );
			totalStolen += take;
		}

		ownerDamage.Heal( totalStolen );

		Log.Info( $"[LavernaPower] {donors.Count} Spender × {perPlayer} HP → {totalStolen} gestohlen, Caster HP={ownerDamage.CurrentHP}/{ownerDamage.MaxHP}" );
	}
}
