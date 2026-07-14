using Sandbox;
using LapSystem;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox;

/// <summary>
/// Which god a player has sacrificed to on the Opferaltar.
/// </summary>
public enum GodId
{
	None = 0,
	Taranis,
	Maat,
	Laverna,
	Dionysos,
}

/// <summary>
/// The always-on level-3 effect a god grants.
/// </summary>
public enum AltarBonus
{
	None = 0,
	Attack,     // Taranis  — +% Attack   (PlayerStats.Attack)
	Defense,    // Ma'at    — +% Defense  (PlayerStats.Defense)
	UltChance,  // Laverna  — +% ULT roll (PlayerRaceModifiers -> ItemPrefab)
	Currency,   // Dionysos — +% PG earn  (PublicityCurrencyManager)
}

/// <summary>
/// Host-authoritative backend for the Opferaltar (sacrifice altar). Players spend PG
/// (see <see cref="PublicityCurrencyManager"/>) between rounds to buy up to 3 upgrade
/// levels for ONE god. Owning a level gives, at the start of the next race:
///   L1 — 10% chance to start holding the god's Ultimate item.
///   L2 — if the ULT roll fails, 45% chance to start holding the god's Normal item.
///   L3 — an always-on god-specific bonus (Attack / Defense / ULT-chance / PG).
/// Levels stack (owning L3 includes the L1+L2 rolls). Each player may only have ONE
/// god upgraded — buying a different god discards the previous one (no refund).
///
/// State model mirrors <see cref="PublicityCurrencyManager"/>: a static store keyed by
/// Steam ID lives for the host-process lifetime, so a player's god + level survive the
/// Lobby &lt;-&gt; Race scene changes. Clients hold a mirror the host pushes via Broadcast
/// RPC. Auto-spawned by <see cref="EnsureExists"/> from GameNetworkManager (lobby) and
/// RaceManager (race), so it needs no manual scene placement.
/// </summary>
public sealed class AltarUpgradeManager : Component
{
	public static AltarUpgradeManager Instance { get; private set; }

	// ---------- Tunables ----------

	public const int MaxLevel = 3;

	/// <summary>Chance (0..1) that L1+ starts the race with the god's ULTIMATE item.</summary>
	public const float UltRollChance = 0.10f;

	/// <summary>Chance (0..1) that L2+ starts with the NORMAL item when the ULT roll fails.</summary>
	public const float NormalRollChance = 0.45f;

	/// <summary>Always-on level-3 bonus magnitude (0.05 = +5%).</summary>
	public const float BonusAmount = 0.05f;

	// Cost to REACH each level (per-level-up, from PG&Balancing / Opferaltar PDF).
	private static int CostForLevel( int level ) => level switch
	{
		1 => 30,
		2 => 80,
		3 => 150,
		_ => 0,
	};

	// Delay after a player's tracker first appears before we grant the start item, so
	// PlayerItemTracker.OnStart (which clears any stale HeldItemKey) has definitely run.
	private const float GrantDelay = 0.75f;

	// ---------- God definitions (code constants) ----------

	private sealed class GodDef
	{
		public GodId Id;
		public string DisplayName;
		public string ItemKey;      // must match ItemPrefab.ItemPool keys
		public AltarBonus Bonus;
		public string BonusText;    // shown in the UI
	}

	private static readonly Dictionary<GodId, GodDef> Gods = new()
	{
		[GodId.Taranis]  = new GodDef { Id = GodId.Taranis,  DisplayName = "Taranis",  ItemKey = "Taranis",  Bonus = AltarBonus.Attack,    BonusText = "+5% Angriff" },
		[GodId.Maat]     = new GodDef { Id = GodId.Maat,     DisplayName = "Ma'at",    ItemKey = "Ma'at",    Bonus = AltarBonus.Defense,   BonusText = "+5% Verteidigung" },
		[GodId.Laverna]  = new GodDef { Id = GodId.Laverna,  DisplayName = "Laverna",  ItemKey = "Laverna",  Bonus = AltarBonus.UltChance, BonusText = "+5% ULT-Chance" },
		[GodId.Dionysos] = new GodDef { Id = GodId.Dionysos, DisplayName = "Dionysos", ItemKey = "Dionysos", Bonus = AltarBonus.Currency,  BonusText = "+5% Publikumsgunst" },
	};

	/// <summary>A player's altar choice: which god and how far upgraded (0 = none).</summary>
	public struct Choice
	{
		public GodId God;
		public int Level;
	}

	// ---------- Authoritative state (host process, survives scene changes) ----------

	private static readonly Dictionary<ulong, Choice> HostStore = new();

	// ---------- Client-visible mirror ----------

	private static readonly Dictionary<ulong, Choice> ClientMirror = new();

	/// <summary>Fires on every peer whenever any player's altar choice changes.</summary>
	public static event Action OnAltarChanged;

	// ---------- Per-scene race-start bookkeeping (host-only, instance-scoped) ----------

	private readonly Dictionary<ulong, float> _seenAt = new();
	private readonly HashSet<ulong> _applied = new();

	/// <summary>True on the host, or when running with no network session (editor/solo).</summary>
	private static bool IsAuthority => !Networking.IsActive || Networking.IsHost;

	// ---------- Lifecycle ----------

	protected override void OnAwake()
	{
		Instance = this;

		if ( Networking.IsHost )
			GameObject.NetworkSpawn();
	}

	protected override void OnStart()
	{
		if ( Networking.IsHost )
		{
			// Re-push everything we know so freshly-loaded clients get current choices.
			foreach ( var kv in HostStore )
				RpcSetChoice( kv.Key, (int)kv.Value.God, kv.Value.Level );
		}
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	/// <summary>
	/// Idempotent host-only create-and-spawn, matching PublicityCurrencyManager.
	/// </summary>
	public static void EnsureExists( Scene scene )
	{
		if ( !Networking.IsHost ) return;
		if ( Instance.IsValid() ) return;
		if ( scene is null ) return;

		var go = scene.CreateObject( enabled: true );
		go.Name = "AltarUpgradeManager";
		go.Components.Create<AltarUpgradeManager>();
	}

	// ---------- Read API (any peer) ----------

	public static Choice GetChoice( ulong steamId )
	{
		if ( steamId == 0 ) return default;
		if ( Networking.IsHost )
			return HostStore.TryGetValue( steamId, out var v ) ? v : default;
		return ClientMirror.TryGetValue( steamId, out var c ) ? c : default;
	}

	/// <summary>The local peer's current altar choice.</summary>
	public static Choice LocalChoice => GetChoice( PublicityCurrencyManager.LocalSteamId );

	/// <summary>
	/// PG cost to buy the NEXT level of <paramref name="god"/> for this player. Returns
	/// -1 if the god is already maxed. Applies the "first favor free while broke" rule.
	/// Buying a different god than the one currently owned costs level 1 (a switch).
	/// </summary>
	public static int CostForNextLevel( ulong steamId, GodId god )
	{
		var cur = GetChoice( steamId );
		int targetLevel = (god == cur.God) ? cur.Level + 1 : 1;
		if ( targetLevel > MaxLevel ) return -1;

		int cost = CostForLevel( targetLevel );
		if ( targetLevel == 1 && PublicityCurrencyManager.GetCurrency( steamId ) == 0 )
			cost = 0;
		return cost;
	}

	public static string DisplayName( GodId god ) => Gods.TryGetValue( god, out var d ) ? d.DisplayName : god.ToString();
	public static string BonusText( GodId god ) => Gods.TryGetValue( god, out var d ) ? d.BonusText : "";

	/// <summary>Map an altar display name ("Maat", "Ma'at", ...) to its <see cref="GodId"/>.</summary>
	public static GodId ParseGod( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return GodId.None;
		var n = name.Trim().Replace( "'", "" ).ToLowerInvariant();
		return n switch
		{
			"taranis" => GodId.Taranis,
			"maat" => GodId.Maat,
			"laverna" => GodId.Laverna,
			"dionysos" => GodId.Dionysos,
			_ => GodId.None,
		};
	}

	// ---------- Purchase (host-authoritative) ----------

	/// <summary>
	/// Buy the next level of <paramref name="godId"/> for the CALLING player. Routed to
	/// the host; the caller is identified via <see cref="Rpc.Caller"/> (or the local
	/// connection when the host itself buys). Spends PG through
	/// <see cref="PublicityCurrencyManager.TryModify"/> and, on success, records the new
	/// choice and broadcasts it. Choosing a different god discards the old one.
	/// </summary>
	[Rpc.Host]
	public void RequestPurchaseRpc( int godId )
	{
		if ( !IsAuthority ) return;

		var god = (GodId)godId;
		if ( god == GodId.None || !Gods.ContainsKey( god ) ) return;

		var conn = Rpc.Calling ? Rpc.Caller : Connection.Local;
		ulong steamId = conn?.SteamId ?? 0UL;
		if ( steamId == 0 ) return;

		var cur = HostStore.TryGetValue( steamId, out var c ) ? c : default;

		int targetLevel = (god == cur.God) ? cur.Level + 1 : 1;
		if ( targetLevel > MaxLevel )
			return; // already maxed on this god

		int cost = CostForLevel( targetLevel );
		if ( targetLevel == 1 && PublicityCurrencyManager.GetCurrency( steamId ) == 0 )
			cost = 0; // first favor is free while broke (e.g. first race)

		if ( cost > 0 && !PublicityCurrencyManager.TryModify( steamId, -cost ) )
			return; // not enough PG

		HostStore[steamId] = new Choice { God = god, Level = targetLevel };
		RpcSetChoice( steamId, (int)god, targetLevel );

		Log.Info( $"[AltarUpgradeManager] {conn?.DisplayName} -> {god} L{targetLevel} (paid {cost} PG)" );
	}

	[Rpc.Broadcast]
	private void RpcSetChoice( ulong steamId, int god, int level )
	{
		ClientMirror[steamId] = new Choice { God = (GodId)god, Level = level };
		OnAltarChanged?.Invoke();
	}

	// ---------- Race-start application (host-only) ----------

	protected override void OnUpdate()
	{
		if ( !IsAuthority ) return;

		// Only relevant in a race scene (the lobby has no RaceManager). Grant the bought
		// start items + apply the level-3 bonuses once per player, after their tracker
		// has settled.
		if ( RaceManager.Instance is null ) return;

		foreach ( var tracker in Scene.GetAllComponents<PlayerItemTracker>() )
		{
			var root = tracker.PlayerRoot.IsValid() ? tracker.PlayerRoot : tracker.GameObject?.Root;
			ulong steamId = PublicityCurrencyManager.ResolveSteamId( root );
			if ( steamId == 0 ) continue;
			if ( _applied.Contains( steamId ) ) continue;

			if ( !_seenAt.TryGetValue( steamId, out var t ) )
			{
				_seenAt[steamId] = Time.Now;
				continue;
			}
			if ( Time.Now - t < GrantDelay ) continue;

			_applied.Add( steamId );
			ApplyLoadout( steamId, root, tracker );
		}
	}

	private void ApplyLoadout( ulong steamId, GameObject root, PlayerItemTracker tracker )
	{
		var choice = HostStore.TryGetValue( steamId, out var c ) ? c : default;
		if ( choice.God == GodId.None || choice.Level <= 0 ) return;
		if ( !Gods.TryGetValue( choice.God, out var def ) ) return;

		// ---- Ability roll: 10% ULT first; if that misses and L2+, 45% Normal ----
		bool granted = false;
		if ( choice.Level >= 1 && Random.Shared.Float( 0f, 1f ) < UltRollChance )
			granted = TryGrantStartItem( tracker, def, isUltimate: true );

		if ( !granted && choice.Level >= 2 && Random.Shared.Float( 0f, 1f ) < NormalRollChance )
			granted = TryGrantStartItem( tracker, def, isUltimate: false );

		// ---- Always-on level-3 bonus ----
		if ( choice.Level >= MaxLevel )
			ApplyBonus( steamId, root, def );

		Log.Info( $"[AltarUpgradeManager] Applied loadout for {steamId}: {def.Id} L{choice.Level} (startItem={granted})" );
	}

	private bool TryGrantStartItem( PlayerItemTracker tracker, GodDef def, bool isUltimate )
	{
		var prefab = ResolveGodPrefab( def.ItemKey );
		if ( !prefab.IsValid() )
		{
			Log.Warning( $"[AltarUpgradeManager] No prefab for '{def.ItemKey}' in any ItemPrefab pool — can't grant start item." );
			return false;
		}

		// Reuse the item system's instant (no-jingle) grant path.
		tracker.GrantItemRpc( def.ItemKey, prefab, isUltimate );
		return true;
	}

	// The god prefabs are already wired into the scene's item boxes, so we source them
	// from there instead of duplicating prefab references on this manager.
	private GameObject ResolveGodPrefab( string key )
	{
		foreach ( var box in Scene.GetAllComponents<ItemPrefab>() )
		{
			if ( box.ItemPool != null && box.ItemPool.TryGetValue( key, out var prefab ) && prefab.IsValid() )
				return prefab;
		}
		return null;
	}

	private void ApplyBonus( ulong steamId, GameObject root, GodDef def )
	{
		switch ( def.Bonus )
		{
			case AltarBonus.Currency:
				PublicityCurrencyManager.SetCurrencyBonusPercent( steamId, BonusAmount );
				break;

			case AltarBonus.Attack:
				PushModifiers( root, attack: BonusAmount );
				break;

			case AltarBonus.Defense:
				PushModifiers( root, defense: BonusAmount );
				break;

			case AltarBonus.UltChance:
				PushModifiers( root, ultChance: BonusAmount );
				break;
		}
	}

	private static void PushModifiers( GameObject root, float attack = 0f, float defense = 0f, float ultChance = 0f )
	{
		var mods = root?.Components.Get<PlayerRaceModifiers>( FindMode.EverythingInSelfAndDescendants );
		if ( mods is null )
		{
			Log.Warning( "[AltarUpgradeManager] Player has no PlayerRaceModifiers — level-3 bonus not applied." );
			return;
		}

		// Routes to the owning client, which writes the [Sync] fields; every peer applies them.
		mods.SetModifiersRpc( attack, defense, ultChance );
	}
}
