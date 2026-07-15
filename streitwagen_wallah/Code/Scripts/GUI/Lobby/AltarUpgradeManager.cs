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
/// Wire format for ONE player's altar state. A separate plain-property class rather than the live
/// PlayerAltar, so System.Text.Json has something straightforward to (de)serialise — same reason
/// <see cref="PlayerNameTable"/> exists alongside the name manager's own dictionaries.
///
/// SteamId is a string, not a ulong, for the same reason PlayerNameTable keys by string: a 64-bit
/// Steam ID is past the point where anything treating JSON numbers as doubles keeps every digit.
/// </summary>
public sealed class AltarPlayerState
{
	public string SteamId { get; set; } = "";

	/// <summary>The Götter-Fluch. See <see cref="AltarUpgradeManager.IsCursed"/>.</summary>
	public bool Cursed { get; set; }

	/// <summary>godId -> rolled <see cref="AltarOption"/>, both as ints.</summary>
	public Dictionary<string, int> Options { get; set; } = new();

	/// <summary>godIds whose rolled favour this player has bought.</summary>
	public List<int> Active { get; set; } = new();
}

/// <summary>
/// The whole altar — every player's favours and purchases — in one payload. See
/// <see cref="AltarUpgradeManager.BroadcastState"/> for why it goes over as a single snapshot
/// instead of a per-god drip.
/// </summary>
public sealed class AltarStateTable
{
	public List<AltarPlayerState> Players { get; set; } = new();

	/// <summary>
	/// The HOST's debug flag. It rides along so a client prices favours exactly the way the host
	/// charges for them — the flag is only meaningful on the authority, and a client that mirrored
	/// its own unticked checkbox into the static would print "30 PG" while the host charged 0.
	/// </summary>
	public bool DebugFreeUpgrades { get; set; }
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
///
/// TWO RULES KEEP THIS MULTIPLAYER-SAFE, and both exist because breaking either one silently gives the
/// HOST what a client asked for — the failure this system originally shipped with:
///
///  * IDENTITY IS ALWAYS EXPLICIT. Anything acting on behalf of a player takes their Steam ID as an
///    argument. Nothing derives the actor from ambient RPC state (<see cref="Rpc.Caller"/> /
///    <see cref="Rpc.Calling"/>), because when that context is absent it doesn't fail loudly — it reads
///    as <see cref="Connection.Local"/>, which ON THE HOST is the host. Every client sacrifice then hit
///    the host's row: the host got the favours, the host ate the curse, and the client's own state never
///    moved, so their UI had nothing to redraw. <see cref="RequestPurchase"/> is the pattern: the host
///    applies directly, a client sends its own ID, and Rpc.Caller is used only to REJECT a mismatch.
///
///  * REPLICATION IS A SNAPSHOT, NOT A DIFF. The host ships the whole store
///    (<see cref="BroadcastState"/>) rather than per-change messages, so a peer that wasn't listening
///    yet is repaired by the next send instead of staying wrong forever.
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
	/// <see cref="ApplyPurchase"/> skips the PG deduction entirely. Toggle it live via the
	/// "Debug – Gratis-Upgrades" checkbox on the AltarGUI component (the AUTHORITY mirrors this flag
	/// each frame; clients receive the host's value in <see cref="AltarStateTable.DebugFreeUpgrades"/>,
	/// so their price labels agree with what the host actually charges).
	/// Enforced on the host (the authority), so it works in solo/host editor sessions.
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

	// How often a client re-asks the host for a snapshot while it still has no favours of its own.
	// See PollForState.
	private const float SyncRetryInterval = 1f;

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
	public static bool IsAuthority => !Networking.IsActive || Networking.IsHost;

	/// <summary>
	/// Set by anything that mutates <see cref="HostStore"/>; flushed once per frame by
	/// <see cref="OnUpdate"/>. Coalescing matters: a single sacrifice can evict an old item god, add
	/// a new one and land a curse, and a re-roll touches four gods for every player at once. One
	/// snapshot per frame means peers can never observe a half-applied purchase.
	/// </summary>
	private bool _stateDirty;

	// Client-side: when we last asked the host for a snapshot. See the pull in OnUpdate.
	private float _lastSyncRequest;

	// Authority-side: last value of DebugFreeUpgrades we sent, so flipping the AltarGUI checkbox
	// reaches the clients' price labels instead of waiting for the next purchase to push it.
	private bool _debugFreeSent;

	// ---------- Lifecycle ----------

	/// <summary>
	/// DIAGNOSE: jede Zustands-Sendung/-Ankunft protokollieren. Beantwortet in EINEM Playtest,
	/// welches Glied der Kette hängt, statt es zu erraten:
	///   * Host zeigt "-&gt; sende", Client zeigt KEIN "&lt;- empfangen"  => das Objekt ist nicht
	///     wirklich genetzwerkt, der Broadcast bleibt lokal (siehe die [Altar/Net]-Zeile).
	///   * Client zeigt "&lt;- empfangen ... verflucht=False", Host hält ihn aber für verflucht
	///     => der Fluch landet auf der falschen Zeile.
	///   * Client zeigt "verflucht=True", aber kein Popup => Fluch ist da, das Problem liegt
	///     in der AltarGUI (Frames/Flanke), nicht im Netzwerk.
	/// </summary>
	public static bool DebugSyncLog { get; set; } = true;

	protected override void OnAwake()
	{
		Instance = this;

		if ( Networking.IsHost )
			GameObject.NetworkSpawn();

		// Die entscheidende Zeile: Network.Active = "Is this object networked?". Ist sie auf dem
		// CLIENT false (oder existiert dieses Log dort gar nicht), dann laufen alle RPCs dieses
		// Managers nur lokal ins Leere – dann ist es kein Fluch-Problem, sondern ein Spawn-Problem.
		if ( DebugSyncLog )
		{
			Log.Info( $"[Altar/Net] IsHost={Networking.IsHost} IsActive={Networking.IsActive} " +
				$"Authority={IsAuthority} | Manager networked={GameObject.Network.Active} " +
				$"IsProxy={GameObject.Network.IsProxy} Owner={GameObject.Network.Owner?.DisplayName ?? "-"} " +
				$"| ich={PublicityCurrencyManager.LocalSteamId}" );
		}
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

		if ( newCycle )
		{
			// Debug: print who came OUT of the last race still cursed, before the re-roll wipes it.
			LogRoster( "Lobby (vor Neu-Würfeln)" );

			foreach ( var kv in HostStore )
				RollFavours( kv.Key, kv.Value );   // rolls + clears purchases
		}

		// Either way every peer gets the current state: the re-roll above needs sending, and a race
		// scene must re-state what the lobby left behind for clients that just loaded in.
		MarkDirty();

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
		MarkDirty();

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
		MarkDirty();
	}

	/// <summary>
	/// Note that the whole store needs re-broadcasting. Never sends immediately — see
	/// <see cref="_stateDirty"/>; <see cref="OnUpdate"/> does the actual send.
	/// </summary>
	private void MarkDirty() => _stateDirty = true;

	/// <summary>
	/// Ship the ENTIRE store to every peer as one snapshot.
	///
	/// This used to be a drip of per-god RPCs (one per god, plus one for the curse), which had two
	/// problems a snapshot doesn't. Ordering: a peer could apply the "god X is now inactive" half of a
	/// free A/B swap without the "god Y is now active" half and render a state that never existed. And
	/// completeness: the drip only ever described what CHANGED, so a peer whose object wasn't ready yet
	/// missed those messages permanently — the client's mirror stayed empty, its price labels stayed
	/// blank, and nothing would ever refill them because the host had no reason to re-send.
	///
	/// A snapshot is idempotent and self-healing: whatever a peer missed, the next one puts right. The
	/// payload is four players' worth of four small ints, so re-sending everything is cheaper than the
	/// bookkeeping needed to avoid it.
	/// </summary>
	private void BroadcastState()
	{
		var table = new AltarStateTable { DebugFreeUpgrades = DebugFreeUpgrades };

		foreach ( var kv in HostStore )
		{
			var dto = new AltarPlayerState
			{
				SteamId = kv.Key.ToString(),
				Cursed = kv.Value.Cursed,
			};

			foreach ( var opt in kv.Value.Options )
				dto.Options[((int)opt.Key).ToString()] = (int)opt.Value;

			foreach ( var god in kv.Value.Active )
				dto.Active.Add( (int)god );

			table.Players.Add( dto );
		}

		var json = Json.Serialize( table );

		if ( DebugSyncLog )
		{
			Log.Info( $"[Altar/Sync] -> sende Schnappschuss: {table.Players.Count} Spieler, {json.Length} Zeichen, " +
				$"networked={GameObject.Network.Active} " +
				$"[{string.Join( "; ", table.Players.Select( p => $"{p.SteamId}:verflucht={p.Cursed},aktiv={p.Active.Count}" ) )}]" );
		}

		RpcSetState( json );
	}

	[Rpc.Broadcast]
	private void RpcSetState( string json ) => ApplyStateFromJson( json );

	/// <summary>
	/// Rebuild the mirror from a host snapshot. Clears first, so a god going inactive or a player
	/// leaving is carried by the absence of an entry rather than needing its own message.
	///
	/// Runs on the host too (Broadcast is local as well), where the mirror is only a shadow of the
	/// authoritative <see cref="HostStore"/> — harmless, and it keeps the curse log identical on
	/// every peer.
	/// </summary>
	private static void ApplyStateFromJson( string json )
	{
		// Snapshot the old curse flags BEFORE the clear: the log below reports transitions, and after
		// a rebuild there is nothing left to compare against.
		var wasCursed = new Dictionary<ulong, bool>();
		foreach ( var kv in ClientMirror )
			wasCursed[kv.Key] = kv.Value.Cursed;

		try
		{
			var table = Json.Deserialize<AltarStateTable>( json );
			if ( table is null ) return;

			ClientMirror.Clear();
			DebugFreeUpgrades = table.DebugFreeUpgrades;

			foreach ( var dto in table.Players )
			{
				if ( !ulong.TryParse( dto.SteamId, out var steamId ) || steamId == 0 ) continue;

				var st = new PlayerAltar { Cursed = dto.Cursed };

				if ( dto.Options != null )
				{
					foreach ( var kv in dto.Options )
						if ( int.TryParse( kv.Key, out var god ) )
							st.Options[(GodId)god] = (AltarOption)kv.Value;
				}

				if ( dto.Active != null )
				{
					foreach ( var god in dto.Active )
						st.Active.Add( (GodId)god );
				}

				ClientMirror[steamId] = st;

				// Only on CHANGE — the host re-sends this snapshot on every join, scene load and
				// purchase, so logging the state itself would scroll a curse line past for every
				// player each time anything at all happened.
				bool before = wasCursed.TryGetValue( steamId, out var b ) && b;
				if ( before != st.Cursed )
					Log.Info( $"[Curse] {NameFor( steamId )} ({steamId}) ist jetzt {(st.Cursed ? "VERFLUCHT" : "sauber")}." );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[AltarUpgradeManager] Konnte Altar-Zustand nicht lesen: {e.Message}" );
			return;
		}

		if ( DebugSyncLog )
		{
			ulong me = PublicityCurrencyManager.LocalSteamId;
			var mine = ClientMirror.TryGetValue( me, out var m ) ? m : null;
			Log.Info( $"[Altar/Sync] <- empfangen: {ClientMirror.Count} Spieler | ich={me} " +
				$"im Schnappschuss={(mine is not null)} verflucht={mine?.Cursed} " +
				$"gewürfelt={mine?.Options.Count ?? 0} aktiv={mine?.Active.Count ?? 0}" );
		}

		OnAltarChanged?.Invoke();
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
	/// broadcasts it via <see cref="RpcSetState"/>), which is what makes it safe for UI, race logic and
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
	/// <see cref="ApplyPurchase"/> calls straight into this rather than re-deriving the price, so
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
		st.Cursed = false;
		PublicityCurrencyManager.SetBounty( steamId, false );

		foreach ( var god in Gods.Keys )
			st.Options[god] = RollOption();

		MarkDirty();

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
	/// Sacrifice to <paramref name="god"/> for the LOCAL player, activating whatever favour that god
	/// rolled for them. The entry point the UI calls; the host applies it directly and a client asks the
	/// host via <see cref="RequestPurchaseRpc"/>. Prices through <see cref="CostForGod"/> and spends via
	/// <see cref="PublicityCurrencyManager.TryModify"/>.
	/// </summary>
	public static void RequestPurchase( GodId god )
	{
		ulong steamId = PublicityCurrencyManager.LocalSteamId;
		if ( steamId == 0 ) return;

		var mgr = Instance;
		if ( !mgr.IsValid() )
		{
			Log.Warning( "[AltarUpgradeManager] Kein Manager in der Szene — Opfer geht ins Leere." );
			return;
		}

		// The host owns the store, so it just applies the purchase. Only a client needs the RPC, and
		// it names ITSELF as the buyer rather than letting the host infer it — same shape as
		// PlayerNameManager.SendNameToHost, and the reason this works where Rpc.Caller didn't.
		if ( IsAuthority )
			mgr.ApplyPurchase( steamId, god );
		else
			mgr.RequestPurchaseRpc( steamId, (int)god );
	}

	[Rpc.Host]
	public void RequestPurchaseRpc( ulong steamId, int godId )
	{
		if ( !IsAuthority ) return;

		// The buyer is the ID the caller sent, NOT whoever the ambient RPC context thinks is calling.
		// This is the whole multiplayer fix: the old code read the caller off Rpc.Caller and fell back
		// to Connection.Local when Rpc.Calling was false — which on the host is the HOST. Every client
		// sacrifice was charged to, activated on, and cursed the host instead of the player who clicked.
		//
		// Rpc.Caller is still used, but only to REJECT: if the engine does tell us who called, a client
		// may only ever buy for itself. When it doesn't, we fall back to trusting the argument rather
		// than silently attributing the purchase to the wrong player.
		if ( Rpc.Calling && Rpc.Caller is not null && Rpc.Caller.SteamId != steamId )
		{
			Log.Warning( $"[AltarUpgradeManager] {Rpc.Caller.SteamId} wollte für {steamId} opfern — abgelehnt." );
			return;
		}

		ApplyPurchase( steamId, (GodId)godId );
	}

	private void ApplyPurchase( ulong steamId, GodId god )
	{
		if ( !IsAuthority ) return;
		if ( god == GodId.None || !Gods.ContainsKey( god ) ) return;
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
				st.Active.Remove( previous );
		}

		st.Active.Add( god );
		MarkDirty();

		Log.Info( $"[AltarUpgradeManager] {NameFor( steamId )} -> {god} (Option {st.Options[god]}, paid {cost} PG, " +
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
		MarkDirty();
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
	/// <see cref="ApplyStateFromJson"/> only logs on CHANGE, and <see cref="ClientMirror"/> is static, so a
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

	/// <summary>
	/// DIAGNOSE, Gegenrichtung zu <see cref="RpcLogRoster"/>: ein CLIENT schreibt in JEDE Konsole,
	/// auch die des Hosts.
	///
	/// Der Grund ist banal, hat uns aber mehrere Runden gekostet: Interessant ist immer der Zustand
	/// auf dem verfluchten Peer, und das ist praktisch nie der Host — der Host protokolliert also
	/// brav gar nichts, während die eine Zahl, auf die es ankommt, in einem Fenster steht, in das
	/// niemand schaut. Broadcast statt <see cref="Rpc.Host"/>, damit es auch dann sichtbar ist, wenn
	/// jemand nur den Client vor sich hat.
	/// </summary>
	[Rpc.Broadcast]
	public void RpcLogDiag( string msg ) => Log.Info( msg );

	// ---------- Race-start application (host-only) ----------

	protected override void OnUpdate()
	{
		if ( !IsAuthority )
		{
			PollForState();
			return;
		}

		// The debug flag is poked straight into the static by the AltarGUI checkbox, so nothing else
		// would ever notice it changed.
		if ( _debugFreeSent != DebugFreeUpgrades )
		{
			_debugFreeSent = DebugFreeUpgrades;
			MarkDirty();
		}

		// Send at most one snapshot per frame, after every mutation this frame has landed.
		if ( _stateDirty )
		{
			_stateDirty = false;
			BroadcastState();
		}

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
	/// Client-side: keep asking the host for a snapshot until we have our OWN letters.
	///
	/// The single OnStart request isn't enough on its own. It is a fire-and-forget RPC sent the moment
	/// this component appears on our machine, and there is no guarantee the host is in a position to
	/// answer usefully yet — the host may not even have rolled for us at that point (EnsureRolled runs
	/// off Connection.All on ITS next frame). A request that arrives a frame too early gets a snapshot
	/// without us in it, and nothing would ever ask again: that is exactly the "prices and labels never
	/// update" the client sees. Retrying until our own entry shows up costs one tiny RPC per second and
	/// stops the moment it works.
	/// </summary>
	private void PollForState()
	{
		if ( HasRolled( PublicityCurrencyManager.LocalSteamId ) )
			return;   // the host has told us about ourselves; nothing left to wait for

		if ( Time.Now - _lastSyncRequest < SyncRetryInterval ) return;

		_lastSyncRequest = Time.Now;
		RequestSyncRpc();
	}

	/// <summary>True once this peer knows what <paramref name="steamId"/>'s gods rolled this cycle.</summary>
	public static bool HasRolled( ulong steamId ) => Lookup( steamId )?.Options.Count > 0;

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
