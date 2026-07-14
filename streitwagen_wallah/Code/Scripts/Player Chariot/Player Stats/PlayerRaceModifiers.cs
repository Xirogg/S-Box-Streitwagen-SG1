using Sandbox;

/// <summary>
/// Per-player, per-race modifier bag written by the Opferaltar's level-3 bonuses.
/// Lives on the player chariot next to <see cref="PlayerStats"/>.
///
/// The values are <c>[Sync]</c> (owner-authoritative) so they replicate to EVERY
/// peer. That matters because combat damage is resolved on the victim's machine,
/// which reads the ATTACKER's <see cref="PlayerStats"/> there (see
/// <c>PlayerDamageSystem.OnCollisionStart</c>) — an owner-local modifier would be
/// invisible to it. By syncing the raw percentages and re-applying them to the
/// local <see cref="PlayerStats"/> on every peer, the attacker's boosted Attack (and
/// each player's boosted Defense) is correct no matter whose machine computes the hit.
///
/// Flow: the host decides the bonuses at race start (AltarUpgradeManager) and pushes
/// them to the owning client via <see cref="SetModifiersRpc"/>; the owner writes the
/// [Sync] fields; all peers apply them.
/// </summary>
public sealed class PlayerRaceModifiers : Component
{
	/// <summary>Additive Attack percentage (0.05 = +5%). Feeds PlayerStats.Attack.SetPercent.</summary>
	[Sync] public float AttackPercent { get; set; }

	/// <summary>Additive Defense percentage (0.05 = +5%). Feeds PlayerStats.Defense.SetPercent.</summary>
	[Sync] public float DefensePercent { get; set; }

	/// <summary>
	/// Extra chance (0..1) added on top of an item box's base <c>UltimateChance</c>
	/// when THIS player picks up an item. Read host-side by <c>ItemPrefab</c>.
	/// </summary>
	[Sync] public float ExtraUltimateChance { get; set; }

	private const string AltarKey = "altar";

	private PlayerStats _stats;

	// Last values pushed into PlayerStats, so we only touch the dictionaries on change.
	private float _appliedAttack = float.NaN;
	private float _appliedDefense = float.NaN;

	private PlayerStats Stats
	{
		get
		{
			if ( _stats.IsValid() ) return _stats;
			_stats = Components.Get<PlayerStats>( FindMode.EverythingInSelfAndAncestors )
				?? GameObject.Root?.Components.Get<PlayerStats>( FindMode.EverythingInSelfAndDescendants );
			return _stats;
		}
	}

	protected override void OnUpdate()
	{
		// Runs on every peer (owner and proxies). Whenever the synced percentages
		// change, mirror them into the local PlayerStats so the value the damage
		// system reads here reflects this player's altar bonus.
		if ( AttackPercent == _appliedAttack && DefensePercent == _appliedDefense )
			return;

		var stats = Stats;
		if ( stats is null ) return;

		stats.Attack.SetPercent( AltarKey, AttackPercent );
		stats.Defense.SetPercent( AltarKey, DefensePercent );

		_appliedAttack = AttackPercent;
		_appliedDefense = DefensePercent;
	}

	protected override void OnDestroy()
	{
		if ( _stats.IsValid() )
		{
			_stats.Attack.ClearPercent( AltarKey );
			_stats.Defense.ClearPercent( AltarKey );
		}
	}

	/// <summary>
	/// Host → owner push of the altar's level-3 bonuses. Runs on the owning client,
	/// which writes the [Sync] fields; every peer then applies them via OnUpdate.
	/// Pass 0 for a bonus this player doesn't have.
	/// </summary>
	[Rpc.Owner]
	public void SetModifiersRpc( float attackPercent, float defensePercent, float extraUltimateChance )
	{
		AttackPercent = attackPercent;
		DefensePercent = defensePercent;
		ExtraUltimateChance = extraUltimateChance;
	}
}
