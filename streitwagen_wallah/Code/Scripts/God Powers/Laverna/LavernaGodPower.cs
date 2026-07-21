using LapSystem.Rankings;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Laverna:
///   Normal (Item-Dieb): pick a random opponent who is currently holding an item
///   (excluding last place) and rip the item out of their tracker into the
///   caster's tracker. The victim loses the GodPower clone immediately, the
///   thief receives the same prefab via the regular grant path.
///
///   Ultimate (Göttlicher Hehler): steal HP from every player richer than the
///   caster, distributed as evenly as possible, until the caster is full again.
///   Donors with a Ma'at Karma Shield refund their full HP back to themselves
///   and the caster eats that as damage instead.
/// </summary>
public sealed class LavernaPower : GodPower
{
	[Property, Group( "Heist" )]
	public string PlayerTag { get; set; } = "player";

	[Property, Group( "Item Dieb" )]
	public bool ExcludeLastPlace { get; set; } = true;

	/// <summary>
	/// Fallback item key that identifies a Laverna power. HeldItemKey is the free-form
	/// editor string from the item box's ItemPool ("Laverna", "Dionysos", ...). We
	/// normally derive Laverna's key from the CASTER's own tracker at cast time (its
	/// slot still reads "Laverna" during OnActivate), so this is only used if that
	/// lookup fails. Compared normalized (letters-only lowercase), the same way the
	/// HUD's IconForKey matches, so casing/spacing variants still resolve.
	/// </summary>
	[Property, Group( "Item Dieb" )]
	public string LavernaItemKey { get; set; } = "Laverna";

	/// <summary>
	/// Short delay between the activation frame and the actual item transfer.
	/// The caster's tracker still has *this* power in its slot the moment we
	/// run OnActivate; deferring by one Invoke gives it time to finalize its
	/// own use-cleanup (FinishUse clears HeldItemKey), so the incoming grant
	/// is not rejected by the HasItem check.
	/// </summary>
	[Property, Group( "Item Dieb" )]
	public float TransferDelay { get; set; } = 0.05f;

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = false;

	// ---------- Normal: Item-Dieb ----------

	protected override void OnActivate()
	{
		if ( !Owner.IsValid() )
		{
			Log.Warning( "[LavernaPower] No owner assigned — Item-Dieb skipped." );
			return;
		}

		// Smoke on the caster's Wagen the moment they cast (whether or not a victim is
		// found), broadcast so everyone sees it. Auto-destroyed by the VFX module.
		ResolveNormalVfx()?.PlayLavernaSteal();

		var thief = Owner.Components.Get<PlayerItemTracker>( FindMode.EverythingInSelfAndDescendants );
		if ( thief is null )
		{
			Log.Warning( "[LavernaPower] Owner has no PlayerItemTracker — Item-Dieb skipped." );
			return;
		}

		Guid lastPlaceId = Guid.Empty;
		if ( ExcludeLastPlace ) lastPlaceId = ResolveLastPlaceId();

		// The key that counts as "a Laverna power". Prefer the caster's OWN slot —
		// during OnActivate the thief's tracker still reads its held key (the
		// use-cleanup that clears it runs after us), so this is the exact editor
		// string in play. Fall back to the configured key if the slot is already empty.
		string lavernaKey = !string.IsNullOrEmpty( thief.HeldItemKey ) ? thief.HeldItemKey : LavernaItemKey;

		// Split eligible victims into two tiers so we can prefer stealing a
		// non-Laverna item and only fall back to robbing another Laverna if that's
		// all that's left. Self-exclusion and ExcludeLastPlace apply to both tiers.
		var nonLavernaCandidates = new List<PlayerItemTracker>();
		var lavernaCandidates = new List<PlayerItemTracker>();
		foreach ( var t in Scene.GetAllComponents<PlayerItemTracker>() )
		{
			if ( t == thief ) continue;
			if ( !t.HasItem ) continue;

			if ( lastPlaceId != Guid.Empty && ResolvePlayerId( t ) == lastPlaceId )
				continue;

			if ( IsLavernaKey( t.HeldItemKey, lavernaKey ) )
				lavernaCandidates.Add( t );
			else
				nonLavernaCandidates.Add( t );
		}

		// Tier 1: prefer non-Laverna items. Tier 2: forced to steal another Laverna.
		var pool = nonLavernaCandidates.Count > 0 ? nonLavernaCandidates : lavernaCandidates;

		// Tier 3: nobody eligible holds anything — steal nothing and say so.
		if ( pool.Count == 0 )
		{
			Log.Info( "[LavernaPower] Item-Dieb found no eligible victim (nobody else is holding an item)." );
			ResolveNotifier()?.Show( "Du konntest nichts klauen" );
			return;
		}

		var victim = pool[Random.Shared.Next( 0, pool.Count )];

		// Read the key now — by the time the deferred transfer runs the victim's
		// slot has been wiped, so we'd lose the name otherwise.
		string stolenKey = victim.HeldItemKey;
		ResolveNotifier()?.Show( string.IsNullOrEmpty( stolenKey )
			? "Du hast ein Item gestohlen"
			: $"Du hast {stolenKey} gestohlen" );

		// Sound A → Sound B, heard only by the caster and the victim ("used one").
		var victimRoot = victim.PlayerRoot.IsValid() ? victim.PlayerRoot : victim.GameObject?.Root;
		ResolveNormalSfx()?.PlayLavernaSteal( victimRoot );

		if ( DebugLog )
			Log.Info( $"[LavernaPower] Stealing '{stolenKey}' from {victim.GameObject?.Name} in {TransferDelay}s." );

		// Capture the references — by the time the lambda fires the Laverna instance
		// itself is gone, but the tracker survives because it lives on the chariot.
		var thiefRef = thief;
		var victimRef = victim;
		thief.Invoke( TransferDelay, () =>
		{
			if ( !victimRef.IsValid() || !thiefRef.IsValid() ) return;
			victimRef.TransferHeldItemRpc( thiefRef );
		} );
	}

	private Guid ResolveLastPlaceId()
	{
		var mgr = RaceRankingManager.Instance;
		if ( mgr is null ) return Guid.Empty;

		var rankings = mgr.Rankings;
		if ( rankings is null || rankings.Count == 0 ) return Guid.Empty;

		// Rankings are sorted with #1 at index 0, so the last entry is the lowest position.
		var last = rankings[rankings.Count - 1];
		return last.PlayerId;
	}

	/// <summary>
	/// True if <paramref name="heldKey"/> names a Laverna power. Both keys are
	/// normalized to letters-only lowercase before comparison (mirroring the HUD's
	/// IconForKey), so "Laverna" / "laverna" / "La verna" all match. Comparing the
	/// [Sync]'d HeldItemKey — rather than the victim's cloned prefab component — is
	/// deliberate: the prefab reference is owner-local runtime state and is null on a
	/// remote victim's proxy tracker, whereas HeldItemKey replicates to every client.
	/// </summary>
	private static bool IsLavernaKey( string heldKey, string lavernaKey )
	{
		return NormalizeKey( heldKey ) == NormalizeKey( lavernaKey )
			&& !string.IsNullOrEmpty( NormalizeKey( heldKey ) );
	}

	private static string NormalizeKey( string key )
	{
		if ( string.IsNullOrEmpty( key ) ) return "";
		return new string( key.ToLowerInvariant().Where( char.IsLetter ).ToArray() );
	}

	private static Guid ResolvePlayerId( PlayerItemTracker tracker )
	{
		var root = tracker.PlayerRoot.IsValid() ? tracker.PlayerRoot : tracker.GameObject?.Root;
		var ctrl = root?.Components.Get<TestControlls>( FindMode.EverythingInSelfAndDescendants );
		return ctrl is not null ? ctrl.PlayerId : Guid.Empty;
	}

	// ---------- Ultimate: Göttlicher Hehler ----------

	protected override void OnActivateUltimate()
	{
		if ( !Owner.IsValid() )
		{
			Log.Warning( "[LavernaPower] No owner assigned — Ultimate skipped." );
			return;
		}

		// Sound A + voice, worldwide.
		GodPowersUltimateSfxmodule.Instance?.PlayLavernaUltimate();
		// Sky image, per-player (each aimed at their own chariot).
		GodPowersImageModule.Instance?.ShowLavernaImage();

		var ownerDamage = Owner.Components.Get<PlayerDamageSystem>( FindMode.EverythingInSelfAndDescendants );
		if ( ownerDamage is null )
		{
			Log.Warning( "[LavernaPower] Owner has no PlayerDamageSystem — Ultimate skipped." );
			return;
		}

		float casterHP = ownerDamage.CurrentHP;
		float deficit = ownerDamage.MaxHP - casterHP;
		if ( deficit <= 0f )
		{
			if ( DebugLog ) Log.Info( "[LavernaPower] Caster already full — nothing to steal." );
			return;
		}

		// Find every other player tagged "player" with strictly more HP than caster.
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
			if ( DebugLog ) Log.Info( "[LavernaPower] No richer opponent — Ultimate finds nothing to steal." );
			return;
		}

		// Karma Shield: protected donors refund themselves and the steal backfires onto the caster.
		for ( int i = donors.Count - 1; i >= 0; i-- )
		{
			var donor = donors[i];
			var shield = donor.GameObject.Components.Get<MaAtKarmaShield>( FindMode.EverythingInSelfAndDescendants );
			if ( shield is null || !shield.TryConsume() ) continue;

			float refund = donor.MaxHP - donor.CurrentHP;
			if ( refund > 0f )
			{
				donor.Heal( refund );
				ownerDamage.Damage( refund );
			}
			donors.RemoveAt( i );
		}

		// Recompute deficit — Karma backfire above may have moved caster HP.
		float remaining = ownerDamage.MaxHP - ownerDamage.CurrentHP;
		if ( remaining <= 0f || donors.Count == 0 )
		{
			if ( DebugLog ) Log.Info( $"[LavernaPower] Ultimate ended after Karma — caster HP={ownerDamage.CurrentHP}/{ownerDamage.MaxHP}, donors left={donors.Count}." );
			return;
		}

		// Even split, rounded up so we always cover the deficit. Cap each take by both
		// the donor's current HP and the still-needed amount so nobody overpays.
		float perPlayer = MathF.Ceiling( remaining / donors.Count );
		float totalStolen = 0f;

		foreach ( var donor in donors )
		{
			if ( remaining <= 0f ) break;

			float take = MathF.Min( perPlayer, donor.CurrentHP );
			take = MathF.Min( take, remaining );
			if ( take <= 0f ) continue;

			donor.Damage( take );
			totalStolen += take;
			remaining -= take;
		}

		ownerDamage.Heal( totalStolen );

		if ( totalStolen > 0f )
			ResolveNotifier()?.Show( $"Du heilst dich um {totalStolen:0} Leben" );

		if ( DebugLog )
			Log.Info( $"[LavernaPower] {donors.Count} donors × ~{perPlayer:F0} HP → stole {totalStolen:F0}, caster now {ownerDamage.CurrentHP:F0}/{ownerDamage.MaxHP:F0}." );
	}
}
