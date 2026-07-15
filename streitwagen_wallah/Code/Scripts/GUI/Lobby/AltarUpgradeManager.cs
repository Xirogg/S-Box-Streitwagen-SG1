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
/// The single random favour a god offers a player for the NEXT race. Replaces the old
/// upgrade levels: instead of buying L1/L2/L3 one after another, every god rolls exactly
/// one of these per player, per race (see <see cref="AltarUpgradeManager.RollFavours"/>).
/// The letters are what the altar UI prints on the pillar's red drum.
/// </summary>
public enum AltarOption
{
	None = 0,
	A,   // 10% — start the race holding the god's ULTIMATE item
	B,   // 45% — start the race holding the god's NORMAL item
	C,   // 45% — always-on +5% god-specific bonus (see AltarBonus)
}

/// <summary>
/// The always-on effect behind <see cref="AltarOption.C"/>.
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
/// Host-authoritative backend for the Opferaltar (sacrifice altar).
///
/// EVERY god independently rolls ONE favour per player before each race — 10% <see cref="AltarOption.A"/>,
/// 45% <see cref="AltarOption.B"/>, 45% <see cref="AltarOption.C"/>. The rolls are per player, so two
/// players looking at the same altar see different letters. All four letters are visible in the UI, and
/// the player then spends PG (see <see cref="PublicityCurrencyManager"/>) to sacrifice to ONE god,
/// activating that god's rolled favour for the next race. A god is simply active or not — there are no
/// levels. Sacrificing to a different god discards the previous one (no refund).
///
/// LIFECYCLE OF A ROLL: favours re-roll every race cycle, which we key off entering the LOBBY (a scene
/// with no <see cref="RaceManager"/>). A race scene must never re-roll — the race has to honour exactly
/// the letters the player saw when they paid. The chosen god itself is sticky: it survives into the next
/// cycle with a freshly rolled favour, and the player only pays again if they want to switch to a god
/// that rolled something better.
///
/// State model mirrors <see cref="PublicityCurrencyManager"/>: a static store keyed by Steam ID lives for
/// the host-process lifetime, so a player's god + favours survive the Lobby &lt;-&gt; Race scene changes.
/// Clients hold a mirror the host pushes via Broadcast RPC. Auto-spawned by <see cref="EnsureExists"/>
/// from GameNetworkManager (lobby) and RaceManager (race), so it needs no manual scene placement.
/// </summary>
public sealed class AltarUpgradeManager : Component, Component.INetworkListener
{
	public static AltarUpgradeManager Instance { get; private set; }

	// ---------- Tunables ----------

	/// <summary>Flat PG cost to sacrifice to a god. No levels, so this is the only price.</summary>
	public const int SacrificeCost = 30;

	/// <summary>
	/// DEBUG: when true, every sacrifice is free — <see cref="CostForGod"/> reports 0 and
	/// <see cref="RequestPurchaseRpc"/> skips the PG deduction entirely. Toggle it live via the
	/// "Debug – Gratis-Upgrades" checkbox on the AltarGUI component (it mirrors this flag each
	/// frame). Enforced on the host (the authority), so it works in solo/host editor sessions.
	/// The "one god only, switching resets" rule still applies.
	/// </summary>
	public static bool DebugFreeUpgrades { get; set; }

	/// <summary>Chance (0..1) a god rolls <see cref="AltarOption.A"/> (its ULTIMATE item).</summary>
	public const float OptionAChance = 0.10f;

	/// <summary>Chance (0..1) a god rolls <see cref="AltarOption.B"/> (its NORMAL item).</summary>
	public const float OptionBChance = 0.45f;

	// AltarOption.C takes the remainder (0.45) — kept implicit so the three always sum to 1.

	/// <summary>Magnitude of the <see cref="AltarOption.C"/> bonus (0.05 = +5%).</summary>
	public const float BonusAmount = 0.05f;

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

	/// <summary>A player's altar state: the god they sacrificed to, plus this cycle's rolled favour per god.</summary>
	private sealed class PlayerAltar
	{
		public GodId God;
		public readonly Dictionary<GodId, AltarOption> Options = new();
	}

	// ---------- Authoritative state (host process, survives scene changes) ----------

	private static readonly Dictionary<ulong, PlayerAltar> HostStore = new();

	// ---------- Client-visible mirror ----------

	private static readonly Dictionary<ulong, PlayerAltar> ClientMirror = new();

	/// <summary>Fires on every peer whenever any player's god or rolled favours change.</summary>
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
		if ( !IsAuthority )
		{
			// Clients pull. This is deliberately belt-and-braces with the OnActive push below:
			// that push covers "we already existed but the host hadn't sent yet", while this pull
			// covers the opposite order — this component replicating to us only AFTER the host's
			// push went out, which would leave us stuck on an empty mirror forever. Both are
			// idempotent, so whichever lands first simply wins.
			RequestSyncRpc();
			return;
		}

		// No RaceManager in the scene => we're in the lobby => a new race cycle begins, so every
		// player's favours re-roll. In a race scene we must NOT re-roll: the race has to honour the
		// exact letters the player saw when they sacrificed. Either way we re-push everything, so
		// freshly-loaded clients get the current god + favours.
		bool newCycle = !RaceManager.Instance.IsValid();

		foreach ( var kv in HostStore )
		{
			if ( newCycle )
				RollFavours( kv.Key, kv.Value );   // rolls AND broadcasts each option
			else
				PushOptions( kv.Key, kv.Value );

			RpcSetGod( kv.Key, (int)kv.Value.God );
		}
	}

	/// <summary>
	/// A (re)joining peer starts with an empty mirror, and our roll broadcasts are fire-and-forget:
	/// <see cref="EnsureRolled"/> deliberately never re-rolls a player it has already rolled, so it
	/// would never re-send their letters either. Without this push, a player who reconnects during a
	/// lobby sees four blank drums and their active god reads as None — and would then pay to
	/// re-pick a favour they can't see. So push the whole store whenever a connection goes active.
	/// This is the same hook GameNetworkManager uses to spawn that player's chariot, so the peer is
	/// ready to receive by the time we get here.
	/// </summary>
	public void OnActive( Connection channel )
	{
		if ( !IsAuthority ) return;
		PushAll();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	/// <summary>
	/// Client -&gt; host: "I exist now, (re)send the current gods + favours." See <see cref="OnStart"/>.
	/// </summary>
	[Rpc.Host]
	public void RequestSyncRpc()
	{
		if ( !IsAuthority ) return;
		PushAll();
	}

	/// <summary>Re-broadcast every known player's god + favours. Idempotent; tiny payload (max 4 players).</summary>
	private void PushAll()
	{
		foreach ( var kv in HostStore )
		{
			PushOptions( kv.Key, kv.Value );
			RpcSetGod( kv.Key, (int)kv.Value.God );
		}
	}

	private void PushOptions( ulong steamId, PlayerAltar st )
	{
		foreach ( var opt in st.Options )
			RpcSetOption( steamId, (int)opt.Key, (int)opt.Value );
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

	// The authority reads its own store; everyone else reads the mirror the host pushed.
	private static PlayerAltar Lookup( ulong steamId )
	{
		if ( steamId == 0 ) return null;
		var store = IsAuthority ? HostStore : ClientMirror;
		return store.TryGetValue( steamId, out var v ) ? v : null;
	}

	/// <summary>The god this player has sacrificed to, or <see cref="GodId.None"/>.</summary>
	public static GodId GetGod( ulong steamId ) => Lookup( steamId )?.God ?? GodId.None;

	/// <summary>
	/// The favour <paramref name="god"/> rolled for this player this race cycle. Returns
	/// <see cref="AltarOption.None"/> until the host has rolled (or for an unknown player/god).
	/// </summary>
	public static AltarOption GetOption( ulong steamId, GodId god )
	{
		var st = Lookup( steamId );
		if ( st is null ) return AltarOption.None;
		return st.Options.TryGetValue( god, out var o ) ? o : AltarOption.None;
	}

	/// <summary>The local peer's currently active god.</summary>
	public static GodId LocalGod => GetGod( PublicityCurrencyManager.LocalSteamId );

	/// <summary>The favour <paramref name="god"/> rolled for the LOCAL player this race cycle.</summary>
	public static AltarOption LocalOption( GodId god ) => GetOption( PublicityCurrencyManager.LocalSteamId, god );

	/// <summary>
	/// PG cost for this player to sacrifice to <paramref name="god"/>. Returns -1 when there is
	/// nothing to buy (unknown god, or it's already this player's active god). Applies the
	/// "first favour free while broke" rule. Mirrors <see cref="RequestPurchaseRpc"/> exactly —
	/// if these two ever disagree the UI lies about the price.
	/// </summary>
	public static int CostForGod( ulong steamId, GodId god )
	{
		if ( god == GodId.None || !Gods.ContainsKey( god ) ) return -1;

		var current = GetGod( steamId );
		if ( current == god ) return -1;             // already active — re-buying does nothing

		if ( DebugFreeUpgrades ) return 0;           // debug: everything free
		if ( IsFirstFavourFree( steamId, current ) ) return 0;

		return SacrificeCost;
	}

	/// <summary>
	/// The pity rule: a player who is flat broke AND has never sacrificed gets their first god
	/// for nothing (e.g. before the first race, when nobody has earned any PG yet).
	///
	/// The "no god yet" half is load-bearing. Without it, any player sitting at exactly 0 PG could
	/// switch gods for free, forever — and because every god's letter is visible, they'd simply
	/// re-pick whichever one rolled A (the ultimate) each race. Buying at 30 PG lands you on 0 PG,
	/// so that state is trivially reachable, not a corner case. The old code got this for free via
	/// its "level 1 only" check; with levels gone it has to be spelled out.
	/// </summary>
	private static bool IsFirstFavourFree( ulong steamId, GodId currentGod )
		=> currentGod == GodId.None && PublicityCurrencyManager.GetCurrency( steamId ) == 0;

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

	// ---------- Rolling (host-only) ----------

	/// <summary>10% A / 45% B / 45% C — C takes whatever the first two don't.</summary>
	private static AltarOption RollOption()
	{
		float r = Random.Shared.Float( 0f, 1f );
		if ( r < OptionAChance ) return AltarOption.A;
		if ( r < OptionAChance + OptionBChance ) return AltarOption.B;
		return AltarOption.C;
	}

	/// <summary>
	/// Roll a fresh favour for EVERY god for one player and broadcast the result. Each god rolls
	/// independently, and this runs per Steam ID, so no two players share a roll.
	/// </summary>
	private void RollFavours( ulong steamId, PlayerAltar st )
	{
		st.Options.Clear();

		foreach ( var god in Gods.Keys )
		{
			var opt = RollOption();
			st.Options[god] = opt;
			RpcSetOption( steamId, (int)god, (int)opt );
		}

		Log.Info( $"[AltarUpgradeManager] Rolled favours for {steamId}: " +
			string.Join( ", ", st.Options.Select( kv => $"{kv.Key}={kv.Value}" ) ) );
	}

	// A player with no favours yet has never been rolled this cycle (OnStart clears + re-rolls
	// everyone it knows about, but players who join later — or who joined before the manager
	// spawned — need catching up). Cheap: the Options.Count guard makes this a no-op once rolled.
	private void EnsureRolled()
	{
		// Indexed loop: this runs every frame, and foreach over the IReadOnlyList interface would
		// heap-allocate an enumerator each time.
		var all = Connection.All;
		for ( int i = 0; i < all.Count; i++ )
			EnsureRolledFor( all[i]?.SteamId ?? 0UL );

		// Solo/editor sessions can have an empty Connection.All; the local player still needs favours.
		EnsureRolledFor( PublicityCurrencyManager.LocalSteamId );
	}

	private void EnsureRolledFor( ulong steamId )
	{
		if ( steamId == 0 ) return;

		if ( !HostStore.TryGetValue( steamId, out var st ) )
			HostStore[steamId] = st = new PlayerAltar();

		if ( st.Options.Count > 0 ) return;   // already rolled for this cycle

		RollFavours( steamId, st );
		RpcSetGod( steamId, (int)st.God );    // seed the mirror's god entry too
	}

	// ---------- Purchase (host-authoritative) ----------

	/// <summary>
	/// Sacrifice to <paramref name="godId"/> for the CALLING player, activating whatever favour that
	/// god rolled for them. Routed to the host; the caller is identified via <see cref="Rpc.Caller"/>
	/// (or the local connection when the host itself buys). Spends PG through
	/// <see cref="PublicityCurrencyManager.TryModify"/> and, on success, records the god and
	/// broadcasts it. Choosing a different god discards the old one.
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

		if ( !HostStore.TryGetValue( steamId, out var st ) )
			HostStore[steamId] = st = new PlayerAltar();

		if ( st.God == god ) return;   // already active — nothing to sell them

		int cost = SacrificeCost;
		if ( IsFirstFavourFree( steamId, st.God ) )
			cost = 0; // broke and godless — see IsFirstFavourFree

		if ( DebugFreeUpgrades )
			cost = 0; // debug: skip the PG cost entirely

		if ( cost > 0 && !PublicityCurrencyManager.TryModify( steamId, -cost ) )
			return; // not enough PG

		// A client that sacrificed before EnsureRolled caught them would otherwise activate an
		// empty favour set, so guarantee the rolls exist before we commit the god.
		if ( st.Options.Count == 0 )
			RollFavours( steamId, st );

		st.God = god;
		RpcSetGod( steamId, (int)god );

		Log.Info( $"[AltarUpgradeManager] {conn?.DisplayName} -> {god} (Option {st.Options[god]}, paid {cost} PG)" );
	}

	[Rpc.Broadcast]
	private void RpcSetGod( ulong steamId, int god )
	{
		MirrorFor( steamId ).God = (GodId)god;
		OnAltarChanged?.Invoke();
	}

	[Rpc.Broadcast]
	private void RpcSetOption( ulong steamId, int god, int option )
	{
		MirrorFor( steamId ).Options[(GodId)god] = (AltarOption)option;
		OnAltarChanged?.Invoke();
	}

	private static PlayerAltar MirrorFor( ulong steamId )
	{
		if ( !ClientMirror.TryGetValue( steamId, out var st ) )
			ClientMirror[steamId] = st = new PlayerAltar();
		return st;
	}

	// ---------- Race-start application (host-only) ----------

	protected override void OnUpdate()
	{
		if ( !IsAuthority ) return;

		// Every player needs their four letters before they can pick, so roll in both scenes —
		// the lobby is where they read them, and a late joiner in a race still needs a valid set.
		EnsureRolled();

		// The rest is race-only (the lobby has no RaceManager): activate the bought god's favour
		// once per player, after their tracker has settled.
		if ( !RaceManager.Instance.IsValid() ) return;

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
		if ( !HostStore.TryGetValue( steamId, out var st ) ) return;
		if ( st.God == GodId.None ) return;
		if ( !Gods.TryGetValue( st.God, out var def ) ) return;

		var option = st.Options.TryGetValue( st.God, out var o ) ? o : AltarOption.None;

		// Exactly one favour fires — the one this god rolled for this player.
		switch ( option )
		{
			case AltarOption.A:
				TryGrantStartItem( tracker, def, isUltimate: true );
				break;

			case AltarOption.B:
				TryGrantStartItem( tracker, def, isUltimate: false );
				break;

			case AltarOption.C:
				ApplyBonus( steamId, root, def );
				break;
		}

		Log.Info( $"[AltarUpgradeManager] Applied loadout for {steamId}: {def.Id} option {option}" );
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
			Log.Warning( "[AltarUpgradeManager] Player has no PlayerRaceModifiers — Option C bonus not applied." );
			return;
		}

		// Routes to the owning client, which writes the [Sync] fields; every peer applies them.
		mods.SetModifiersRpc( attack, defense, ultChance );
	}
}
