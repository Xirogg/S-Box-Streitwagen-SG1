using Sandbox;
using LapSystem;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox;

/// <summary>
/// Which gods a player has sacrificed to on the Opferaltar.
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
/// the player then spends PG (see <see cref="PublicityCurrencyManager"/>) to sacrifice to as MANY gods as
/// they can afford, activating each one's rolled favour for the next race.
///
/// TWO RULES SHAPE WHAT A PLAYER CAN HOLD:
///  * ITEM SLOT — <see cref="AltarOption.A"/> and <see cref="AltarOption.B"/> both hand out a start item,
///    and a player can only start a race holding one thing. So across ALL gods at most ONE A-or-B favour
///    is ever active (<see cref="ActiveItemGod"/>). Sacrificing to a second A/B god is a free SWAP: no PG
///    changes hands, the new god arms and the old one disarms.
///  * BONUS SLOTS — <see cref="AltarOption.C"/> is a passive percentage, so those stack without limit.
///    A player who rolled C on all four gods can buy all four and run every bonus at once.
///
/// PRICE climbs with how much is already active, not with which god: see <see cref="SacrificeCosts"/>.
/// The free A/B swap sidesteps the ladder entirely — swapping never changes the active count.
///
/// CURSE (Götter-Fluch): greed is punished. Every rung of the price ladder carries a curse chance —
/// see <see cref="CurseChances"/> — rolled the instant the sacrifice goes through. The first favour is
/// always safe (0%); the fourth is near-certain (90%). The curse is a single bool per player
/// (<see cref="IsCursed"/>), replicated to every peer, and it survives the Lobby -&gt; Race scene change
/// because it is what hands the player a Kopfgeld (bounty) at race start — see <see cref="ApplyBounties"/>
/// and <see cref="PublicityCurrencyManager.SetBounty"/>. Like purchases, it clears on the next re-roll,
/// so a curse costs you exactly one race.
///
/// LIFECYCLE OF A ROLL: favours re-roll every race cycle, which we key off entering the LOBBY (a scene
/// with no <see cref="RaceManager"/>). A race scene must never re-roll — the race has to honour exactly
/// the letters the player saw when they paid. A re-roll also CLEARS every purchase: each race is bought
/// from scratch off fresh letters, starting again at the cheapest rung of the ladder. That is what keeps
/// the ladder meaningful (it is a recurring choice, not a one-off shopping list) and it is also why the
/// item-slot rule can't be violated by a re-roll — nothing survives a roll to collide with anything.
///
/// State model mirrors <see cref="PublicityCurrencyManager"/>: a static store keyed by Steam ID lives for
/// the host-process lifetime, so a player's purchases + favours survive the Lobby &lt;-&gt; Race scene
/// change (the roll happens in the lobby, the race must read what was bought there). Clients hold a mirror
/// the host pushes via Broadcast RPC. Auto-spawned by <see cref="EnsureExists"/> from GameNetworkManager
/// (lobby) and RaceManager (race), so it needs no manual scene placement.
/// </summary>
public sealed class AltarUpgradeManager : Component, Component.INetworkListener
{
	public static AltarUpgradeManager Instance { get; private set; }

	// ---------- Tunables ----------

	/// <summary>
	/// PG cost of the Nth simultaneous favour, indexed by how many the player already has active.
	/// The first is cheap enough to be an easy habit; the fourth is meant to hurt, so running all
	/// four bonuses at once is a whole race's earnings rather than a default.
	///
	/// The length is also the hard cap on simultaneous favours — four entries for four gods, so a
	/// player who rolled C everywhere can still buy the lot. If a god is ever added, add a rung.
	/// </summary>
	public static readonly int[] SacrificeCosts = { 30, 80, 150, 300 };

	/// <summary>
	/// Chance (0..1) that the Nth simultaneous favour curses the player, indexed by how many they
	/// already have active — the SAME index as <see cref="SacrificeCosts"/>, so the two tables read as
	/// one row per rung: 30 PG/0%, 80 PG/50%, 150 PG/75%, 300 PG/90%.
	///
	/// The first favour being free of risk is what makes the ladder a real decision rather than a tax:
	/// everyone can take one favour every race and never be cursed, so buying a second is the first
	/// moment the player actually chooses to gamble. Rolled by <see cref="TryCurse"/> immediately after
	/// the sacrifice, never on the free A/B swap (a swap buys nothing — see <see cref="IsFreeSwap"/>).
	/// </summary>
	public static readonly float[] CurseChances = { 0.00f, 0.50f, 0.75f, 0.90f };

	/// <summary>
	/// DEBUG: when true, every sacrifice is free — <see cref="CostForGod"/> reports 0 and
	/// <see cref="RequestPurchaseRpc"/> skips the PG deduction entirely. Toggle it live via the
	/// "Debug – Gratis-Upgrades" checkbox on the AltarGUI component (it mirrors this flag each
	/// frame). Enforced on the host (the authority), so it works in solo/host editor sessions.
	/// The item-slot rule still applies — free favours are still only one A/B at a time.
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

	/// <summary>A player's altar state: the gods they sacrificed to, plus this cycle's rolled favour per god.</summary>
	private sealed class PlayerAltar
	{
		/// <summary>Gods whose rolled favour is live for the next race. At most one of them rolled A/B.</summary>
		public readonly HashSet<GodId> Active = new();
		public readonly Dictionary<GodId, AltarOption> Options = new();

		/// <summary>
		/// The Götter-Fluch. Set by <see cref="TryCurse"/> when a sacrifice loses its <see cref="CurseChances"/>
		/// roll, cleared by <see cref="RollFavours"/> on the next cycle. Turns into a Kopfgeld at race start.
		/// </summary>
		public bool Cursed;
	}

	// ---------- Authoritative state (host process, survives scene changes) ----------

	private static readonly Dictionary<ulong, PlayerAltar> HostStore = new();

	// ---------- Client-visible mirror ----------

	private static readonly Dictionary<ulong, PlayerAltar> ClientMirror = new();

	/// <summary>Fires on every peer whenever any player's purchases or rolled favours change.</summary>
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
		// player's favours re-roll and their purchases clear. In a race scene we must NOT re-roll:
		// the race has to honour the exact letters the player paid for. Either way we re-push
		// everything, so freshly-loaded clients get the current purchases + favours.
		bool newCycle = !RaceManager.Instance.IsValid();

		// Debug: print who came OUT of the last race still cursed, before the re-roll wipes it.
		if ( newCycle )
			LogRoster( "Lobby (vor Neu-Würfeln)" );

		foreach ( var kv in HostStore )
		{
			if ( newCycle )
				RollFavours( kv.Key, kv.Value );   // rolls, clears purchases AND broadcasts
			else
				PushPlayer( kv.Key, kv.Value );
		}

		// "If a player is cursed and goes to Race, he gets a Bounty" — this is that moment. The curse
		// was rolled back in the lobby and rode here in the static store; the Kopfgeld only exists in a
		// race, because ramming is the only thing that reads it.
		if ( !newCycle )
			ApplyBounties( "Race-Start" );
	}

	/// <summary>
	/// A (re)joining peer starts with an empty mirror, and our roll broadcasts are fire-and-forget:
	/// <see cref="EnsureRolled"/> deliberately never re-rolls a player it has already rolled, so it
	/// would never re-send their letters either. Without this push, a player who reconnects during a
	/// lobby sees four blank drums and no purchases — and would then pay again for favours they can't
	/// see. So push the whole store whenever a connection goes active. This is the same hook
	/// GameNetworkManager uses to spawn that player's chariot, so the peer is ready to receive by the
	/// time we get here.
	/// </summary>
	public void OnActive( Connection channel )
	{
		if ( !IsAuthority ) return;
		PushAll();

		// A player who joins (or reconnects) once the race is already running missed the OnStart pass,
		// and their curse outlived the disconnect in the static store — so re-state the bounties. Curses
		// can't change during a race (the altar UI is lobby-only), so OnStart + this covers every case.
		if ( RaceManager.Instance.IsValid() )
			ApplyBounties( $"Join: {PlayerNameManager.GetDisplayName( channel )}" );
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	/// <summary>
	/// Client -&gt; host: "I exist now, (re)send the current purchases + favours." See <see cref="OnStart"/>.
	/// </summary>
	[Rpc.Host]
	public void RequestSyncRpc()
	{
		if ( !IsAuthority ) return;
		PushAll();
	}

	/// <summary>Re-broadcast every known player's purchases + favours. Idempotent; tiny payload (max 4 players).</summary>
	private void PushAll()
	{
		foreach ( var kv in HostStore )
			PushPlayer( kv.Key, kv.Value );
	}

	private void PushPlayer( ulong steamId, PlayerAltar st )
	{
		foreach ( var god in st.Options.Keys )
			PushGod( steamId, st, god );

		// Outside the per-god loop on purpose: the curse is one flag for the whole player, and it must
		// still reach a peer whose Options are somehow empty (nothing rolled yet) — otherwise a cursed
		// player could show up clean on a client that joined at an awkward moment.
		RpcSetCursed( steamId, st.Cursed );
	}

	/// <summary>
	/// Send one god's full per-player state (letter + bought-or-not). Everything the host changes goes
	/// out through here, so the mirror can never drift into a half-updated god.
	/// </summary>
	private void PushGod( ulong steamId, PlayerAltar st, GodId god )
	{
		var option = st.Options.TryGetValue( god, out var o ) ? o : AltarOption.None;
		RpcSetGodState( steamId, (int)god, (int)option, st.Active.Contains( god ) );
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

	/// <summary>True when this player has sacrificed to <paramref name="god"/> for the next race.</summary>
	public static bool IsActive( ulong steamId, GodId god )
		=> Lookup( steamId )?.Active.Contains( god ) ?? false;

	/// <summary>How many favours this player currently has live. Indexes <see cref="SacrificeCosts"/>.</summary>
	public static int ActiveCount( ulong steamId ) => Lookup( steamId )?.Active.Count ?? 0;

	/// <summary>
	/// True when the gods have cursed this player for the coming race. Readable on EVERY peer (the host
	/// broadcasts it via <see cref="RpcSetCursed"/>), which is what makes it safe for UI, race logic and
	/// client-side debug alike — unlike <see cref="PublicityCurrencyManager.HasBounty"/>, which is
	/// host-only. In a race the two mean the same thing: cursed players are exactly the bounty carriers.
	/// </summary>
	public static bool IsCursed( ulong steamId ) => Lookup( steamId )?.Cursed ?? false;

	/// <summary>Curse chance (0..1) of the Nth favour, where <paramref name="tier"/> = favours already active.</summary>
	public static float CurseChanceForTier( int tier )
	{
		if ( tier < 0 || CurseChances.Length == 0 ) return 0f;
		if ( tier >= CurseChances.Length ) return CurseChances[^1];
		return CurseChances[tier];
	}

	/// <summary>
	/// The risk this player takes on their NEXT sacrifice. 0 once they're already cursed — the curse is a
	/// bool, so there is nothing left to lose. Exposed for a future risk display on the altar UI.
	/// </summary>
	public static float CurseChanceForNextPurchase( ulong steamId )
		=> IsCursed( steamId ) ? 0f : CurseChanceForTier( ActiveCount( steamId ) );

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

	/// <summary>A or B — the two favours that hand out a start item, and so compete for the one item slot.</summary>
	public static bool IsItemFavour( AltarOption option )
		=> option == AltarOption.A || option == AltarOption.B;

	/// <summary>
	/// The god currently holding this player's single item slot (its favour rolled A or B), or
	/// <see cref="GodId.None"/>. Buying another A/B god swaps this one out for free.
	/// </summary>
	public static GodId ActiveItemGod( ulong steamId )
	{
		var st = Lookup( steamId );
		if ( st is null ) return GodId.None;

		foreach ( var god in st.Active )
			if ( IsItemFavour( st.Options.TryGetValue( god, out var o ) ? o : AltarOption.None ) )
				return god;

		return GodId.None;
	}

	/// <summary>
	/// True when sacrificing to <paramref name="god"/> would be a free item-slot swap: this god rolled
	/// A/B, isn't active yet, and some OTHER god is already holding the slot. The UI prints "Wechseln"
	/// for exactly this case, and <see cref="CostForGod"/> prices it at 0.
	/// </summary>
	public static bool IsFreeSwap( ulong steamId, GodId god )
	{
		if ( god == GodId.None || IsActive( steamId, god ) ) return false;
		if ( !IsItemFavour( GetOption( steamId, god ) ) ) return false;

		var holder = ActiveItemGod( steamId );
		return holder != GodId.None && holder != god;
	}

	// ---------- Local-player shortcuts (the UI reads these) ----------

	public static bool LocalIsActive( GodId god ) => IsActive( PublicityCurrencyManager.LocalSteamId, god );
	public static bool LocalIsFreeSwap( GodId god ) => IsFreeSwap( PublicityCurrencyManager.LocalSteamId, god );

	/// <summary>True when the LOCAL player is carrying the Götter-Fluch into the next race.</summary>
	public static bool LocalIsCursed => IsCursed( PublicityCurrencyManager.LocalSteamId );

	/// <summary>The curse risk the LOCAL player takes on their next sacrifice (0..1).</summary>
	public static float LocalCurseChanceForNextPurchase
		=> CurseChanceForNextPurchase( PublicityCurrencyManager.LocalSteamId );

	/// <summary>The favour <paramref name="god"/> rolled for the LOCAL player this race cycle.</summary>
	public static AltarOption LocalOption( GodId god ) => GetOption( PublicityCurrencyManager.LocalSteamId, god );

	/// <summary>
	/// PG cost for this player to sacrifice to <paramref name="god"/>. Returns -1 when there is nothing
	/// to buy: an unknown god, one that's already active, or a player who has filled every slot.
	/// Returns 0 for a free item-slot swap, the broke-player pity favour, or debug mode.
	///
	/// <see cref="RequestPurchaseRpc"/> calls straight into this rather than re-deriving the price, so
	/// the number the UI prints and the number the host charges cannot drift apart.
	/// </summary>
	public static int CostForGod( ulong steamId, GodId god )
	{
		if ( god == GodId.None || !Gods.ContainsKey( god ) ) return -1;
		if ( IsActive( steamId, god ) ) return -1;      // already bought — re-buying does nothing

		// Checked BEFORE the ladder: a swap doesn't add a favour, it moves one, so it must never be
		// priced off the active count (and must stay free even for a player who owns nothing else).
		if ( IsFreeSwap( steamId, god ) ) return 0;

		if ( DebugFreeUpgrades ) return 0;              // debug: everything free
		if ( IsFirstFavourFree( steamId ) ) return 0;

		int tier = ActiveCount( steamId );
		if ( tier >= SacrificeCosts.Length ) return -1; // every slot spent
		return SacrificeCosts[tier];
	}

	/// <summary>
	/// The pity rule: a player who is flat broke AND has bought nothing this cycle gets their first
	/// favour for nothing (e.g. before the first race, when nobody has earned any PG yet).
	///
	/// The "bought nothing" half is load-bearing. Without it, any player sitting at exactly 0 PG could
	/// keep claiming free favours until they held all four — and because every god's letter is visible,
	/// they'd hoover up whatever rolled well. Buying at 30 PG lands you on 0 PG, so that state is
	/// trivially reachable, not a corner case.
	/// </summary>
	private static bool IsFirstFavourFree( ulong steamId )
		=> ActiveCount( steamId ) == 0 && PublicityCurrencyManager.GetCurrency( steamId ) == 0;

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
	/// Start a fresh cycle for one player: drop last race's purchases, roll a new favour for EVERY god,
	/// and broadcast the lot. Each god rolls independently, and this runs per Steam ID, so no two
	/// players share a roll.
	///
	/// Clearing <see cref="PlayerAltar.Active"/> here is what makes each race bought from scratch — and
	/// it's also why nothing downstream has to police the item slot across cycles: a purchase never
	/// outlives the letter it was made against.
	/// </summary>
	private void RollFavours( ulong steamId, PlayerAltar st )
	{
		st.Active.Clear();
		st.Options.Clear();

		// The curse is bought with the purchases, so it dies with them: one cursed race, then a clean
		// slate. Dropping the bounty here too keeps the two from drifting apart — a player who is no
		// longer cursed must not walk into the next race still carrying last race's Kopfgeld.
		if ( st.Cursed )
		{
			st.Cursed = false;
			RpcSetCursed( steamId, false );
		}
		PublicityCurrencyManager.SetBounty( steamId, false );

		foreach ( var god in Gods.Keys )
		{
			st.Options[god] = RollOption();
			PushGod( steamId, st, god );
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
	}

	// ---------- Purchase (host-authoritative) ----------

	/// <summary>
	/// Sacrifice to <paramref name="godId"/> for the CALLING player, activating whatever favour that god
	/// rolled for them. Routed to the host; the caller is identified via <see cref="Rpc.Caller"/> (or the
	/// local connection when the host itself buys). Prices through <see cref="CostForGod"/> and spends via
	/// <see cref="PublicityCurrencyManager.TryModify"/>.
	///
	/// A god that rolled C simply joins the active set. A god that rolled A/B takes the item slot, which
	/// evicts whoever held it — that eviction is the whole reason the swap is free, so the player is never
	/// charged twice for the one item they get to start with.
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

		// A client that sacrificed before EnsureRolled caught them would otherwise buy an empty
		// favour, so guarantee the rolls exist before we price anything off them.
		if ( st.Options.Count == 0 )
			RollFavours( steamId, st );

		int cost = CostForGod( steamId, god );
		if ( cost < 0 ) return;   // already active, or every slot spent

		// Both are read BEFORE anything mutates, because both describe the ladder rung this sacrifice is
		// being made ON. Once st.Active has grown, ActiveCount reports the rung ABOVE — pricing already
		// happened above for the same reason, and the curse has to be rolled against the same rung the
		// player was charged for, or the odds silently shift one column right.
		int tier = ActiveCount( steamId );
		bool wasSwap = IsFreeSwap( steamId, god );

		if ( cost > 0 && !PublicityCurrencyManager.TryModify( steamId, -cost ) )
			return; // not enough PG

		// Only one start item per race: the incoming A/B favour takes the slot off the old holder.
		// Runs before the Add so ActiveItemGod can't find the newcomer and evict it instead.
		if ( IsItemFavour( st.Options[god] ) )
		{
			var previous = ActiveItemGod( steamId );
			if ( previous != GodId.None )
			{
				st.Active.Remove( previous );
				PushGod( steamId, st, previous );
			}
		}

		st.Active.Add( god );
		PushGod( steamId, st, god );

		Log.Info( $"[AltarUpgradeManager] {conn?.DisplayName} -> {god} (Option {st.Options[god]}, paid {cost} PG, " +
			$"now active: {string.Join( ", ", st.Active )})" );

		// "Chance always needs to be applied directly after purchase" — so roll here, not at race start.
		// A swap is exempt: it moves the item slot instead of buying a favour, costs nothing, and leaves
		// the active count untouched, so there is no new rung to gamble on.
		if ( !wasSwap )
			TryCurse( steamId, st, tier );
	}

	/// <summary>
	/// Roll this sacrifice's <see cref="CurseChances"/> rung and curse the player if it comes up.
	///
	/// Skipped once the player is already cursed: the curse is a bool, so a second one would change
	/// nothing — and rolling anyway would only spam the log with results that can't matter.
	/// </summary>
	private void TryCurse( ulong steamId, PlayerAltar st, int tier )
	{
		if ( st.Cursed ) return;

		float chance = CurseChanceForTier( tier );
		if ( chance <= 0f ) return;   // first favour is always safe

		bool cursed = Random.Shared.Float( 0f, 1f ) < chance;

		Log.Info( $"[Curse] {NameFor( steamId )} rolled favour #{tier + 1} at {chance:P0} risk -> " +
			$"{(cursed ? "VERFLUCHT" : "sicher")}" );

		if ( !cursed ) return;

		st.Cursed = true;
		RpcSetCursed( steamId, true );
	}

	[Rpc.Broadcast]
	private void RpcSetGodState( ulong steamId, int god, int option, bool active )
	{
		var st = MirrorFor( steamId );
		st.Options[(GodId)god] = (AltarOption)option;

		if ( active ) st.Active.Add( (GodId)god );
		else st.Active.Remove( (GodId)god );

		OnAltarChanged?.Invoke();
	}

	/// <summary>
	/// Push one player's curse flag to every peer. Also the DEBUG hook the lobby relies on: it fires on
	/// each machine the moment a curse lands, so every player sees who just got hit without needing a
	/// visual indicator on the altar.
	/// </summary>
	[Rpc.Broadcast]
	private void RpcSetCursed( ulong steamId, bool cursed )
	{
		var st = MirrorFor( steamId );

		// PushAll/PushPlayer re-send the whole store on every join and scene load, so most calls here
		// carry state the peer already has. Bail on those: otherwise a single player joining would print
		// a curse line for everyone in the lobby, and "X is now clean" would scroll past every scene load.
		if ( st.Cursed == cursed ) return;

		st.Cursed = cursed;

		Log.Info( $"[Curse] {NameFor( steamId )} ({steamId}) ist jetzt {(cursed ? "VERFLUCHT" : "sauber")}." );

		OnAltarChanged?.Invoke();
	}

	private static PlayerAltar MirrorFor( ulong steamId )
	{
		if ( !ClientMirror.TryGetValue( steamId, out var st ) )
			ClientMirror[steamId] = st = new PlayerAltar();
		return st;
	}

	/// <summary>
	/// Best-effort display name for logs. Falls back to the raw Steam ID for a player who has
	/// disconnected (their altar state outlives their Connection in the static store).
	/// </summary>
	private static string NameFor( ulong steamId )
	{
		var all = Connection.All;
		for ( int i = 0; i < all.Count; i++ )
		{
			if ( all[i]?.SteamId == steamId )
				return PlayerNameManager.GetDisplayName( all[i] );
		}
		return steamId.ToString();
	}

	// ---------- Curse -> Bounty (host-only) ----------

	/// <summary>
	/// Hand every cursed player a Kopfgeld for this race, and explicitly clear it for everyone else.
	///
	/// The "everyone else" half is the point: bounties are re-stated from scratch here rather than
	/// cleared somewhere and set here, so there is no ordering dependency on
	/// <see cref="PublicityCurrencyManager"/>'s own per-race reset (component OnStart order across
	/// GameObjects isn't guaranteed) and no way for a stale bounty to survive into a race the player
	/// isn't cursed for. Idempotent, so calling it again for a late joiner is free.
	/// </summary>
	private void ApplyBounties( string context )
	{
		foreach ( var kv in HostStore )
			PublicityCurrencyManager.SetBounty( kv.Key, kv.Value.Cursed );

		LogRoster( context );
	}

	/// <summary>
	/// DEBUG: dump every known player's curse/bounty state to EVERY peer's console.
	///
	/// Composed on the host (only it can see the bounty set and the authoritative store) and shipped as
	/// one finished string, because the clients cannot re-derive this: their mirror carries the curse but
	/// never the Kopfgeld. Broadcasting also covers the case that would otherwise go silent on a client —
	/// <see cref="RpcSetCursed"/> only logs on CHANGE, and <see cref="ClientMirror"/> is static, so a
	/// client walking a curse from the lobby into a race sees no change and would print nothing at all.
	/// </summary>
	private void LogRoster( string context )
	{
		if ( HostStore.Count == 0 )
		{
			Log.Info( $"[Curse/{context}] Noch keine Spieler bekannt." );
			return;
		}

		var lines = HostStore.Select( kv =>
			$"{NameFor( kv.Key )}: verflucht={kv.Value.Cursed}, Kopfgeld={PublicityCurrencyManager.HasBounty( kv.Key )}, " +
			$"Gaben={kv.Value.Active.Count}, PG={PublicityCurrencyManager.GetCurrency( kv.Key )}" );

		RpcLogRoster( context, string.Join( "  |  ", lines ) );
	}

	[Rpc.Broadcast]
	private void RpcLogRoster( string context, string summary )
	{
		Log.Info( $"[Curse/{context}] {summary}" );
	}

	// ---------- Race-start application (host-only) ----------

	protected override void OnUpdate()
	{
		if ( !IsAuthority ) return;

		// Every player needs their four letters before they can pick, so roll in both scenes —
		// the lobby is where they read them, and a late joiner in a race still needs a valid set.
		EnsureRolled();

		// The rest is race-only (the lobby has no RaceManager): activate the bought favours
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

	/// <summary>
	/// Fire every favour this player bought. The C bonuses are summed into ONE modifier push rather than
	/// applied one at a time, because <see cref="PlayerRaceModifiers.SetModifiersRpc"/> overwrites all
	/// three percentages at once — pushing per god would leave only the last one standing.
	/// </summary>
	private void ApplyLoadout( ulong steamId, GameObject root, PlayerItemTracker tracker )
	{
		if ( !HostStore.TryGetValue( steamId, out var st ) ) return;

		float attack = 0f, defense = 0f, ultChance = 0f, currency = 0f;

		foreach ( var god in st.Active )
		{
			if ( !Gods.TryGetValue( god, out var def ) ) continue;
			var option = st.Options.TryGetValue( god, out var o ) ? o : AltarOption.None;

			switch ( option )
			{
				case AltarOption.A:
					TryGrantStartItem( tracker, def, isUltimate: true );
					break;

				case AltarOption.B:
					TryGrantStartItem( tracker, def, isUltimate: false );
					break;

				case AltarOption.C:
					switch ( def.Bonus )
					{
						case AltarBonus.Attack: attack += BonusAmount; break;
						case AltarBonus.Defense: defense += BonusAmount; break;
						case AltarBonus.UltChance: ultChance += BonusAmount; break;
						case AltarBonus.Currency: currency += BonusAmount; break;
					}
					break;
			}
		}

		// Unconditional: passing 0 removes the entry, so a player who dropped Dionysos this cycle
		// doesn't keep last race's PG bonus.
		PublicityCurrencyManager.SetCurrencyBonusPercent( steamId, currency );

		if ( attack != 0f || defense != 0f || ultChance != 0f )
			PushModifiers( root, attack, defense, ultChance );

		Log.Info( $"[AltarUpgradeManager] Applied loadout for {steamId}: " +
			string.Join( ", ", st.Active.Select( g => $"{g}={st.Options.GetValueOrDefault( g )}" ) ) );
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
