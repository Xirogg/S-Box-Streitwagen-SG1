using Sandbox;
using LapSystem;
using LapSystem.Rankings;
using System;
using System.Collections.Generic;

/// <summary>
/// Host-authoritative currency ("Publikumsgunst" / PG) system.
///
/// Storage lives in a static dictionary keyed by Steam ID, so a player's PG
/// survives scene changes (Lobby &lt;-&gt; Race) for the lifetime of the host
/// process. Clients hold a local mirror that the host pushes via Broadcast RPC.
///
/// Rewards follow PG&amp;Balancing_v2.docx (Page 1):
///   - Finish rewards: 1st=100, 2nd=80, 3rd=60, 4th-5th=40, 6th-8th=20
///   - +5 PG per ability hit on an opponent
///   - +5 PG per physical Q/E ram hit
///   - Bounty: +20 PG one-time to attacker, -5 PG one-time per attacker to the cursed player
///   - Combat/ability bonuses are capped at +50 PG per race
///   - Total currency never drops below 0
///
/// Auto-spawned by <see cref="EnsureExists"/> from <see cref="GameNetworkManager"/>
/// (lobby) and <see cref="RaceManager"/> (race), so it doesn't need to be placed
/// in every scene manually.
/// </summary>
public sealed class PublicityCurrencyManager : Component
{
	public static PublicityCurrencyManager Instance { get; private set; }

	// ---------- Tunables ----------

	public const int BonusCapPerRace   = 50;
	public const int AbilityHitReward  = 5;
	public const int RamHitReward      = 5;
	public const int BountyHitReward   = 20;
	public const int BountyVictimMalus = 5;

	// Index = finish position. 0 unused. 4..5 share, 6..8 share.
	private static readonly int[] FinishRewardTable =
	{
		0,   // unused
		100, // 1st
		80,  // 2nd
		60,  // 3rd
		40,  // 4th
		40,  // 5th
		20,  // 6th
		20,  // 7th
		20,  // 8th
	};

	// ---------- Authoritative state (host process, survives scene changes) ----------

	private static readonly Dictionary<ulong, int> HostStore = new();

	// Per-race state. Cleared on every scene load (see OnStart).
	private static readonly Dictionary<ulong, int> RaceBonusEarned = new();
	private static readonly Dictionary<ulong, HashSet<ulong>> BountyMalusAppliedBy = new();
	private static readonly HashSet<ulong> Bounties = new();
	private static readonly HashSet<ulong> FinishRewarded = new();

	// Per-player PG-earn bonus (Dionysos Opferaltar level-3 "+5% PG"). 0.05 = +5%.
	// Host-only; set fresh each race by AltarUpgradeManager, so it's cleared on load.
	private static readonly Dictionary<ulong, float> CurrencyBonusPercent = new();

	// ---------- Client-visible mirror ----------

	private static readonly Dictionary<ulong, int> ClientMirror = new();

	/// <summary> Fires on every peer whenever a player's PG total changes. (steamId, newAmount) </summary>
	public static event Action<ulong, int> OnCurrencyChanged;

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
			// Re-send everything we already know so freshly-loaded clients have current totals.
			foreach ( var kv in HostStore )
				RpcSetCurrency( kv.Key, kv.Value );

			// Every scene load is treated as a fresh "race state": bonus caps reset,
			// bounty malus tracking resets. Bounties themselves persist across scenes
			// because the curse is supposed to follow the player until consumed.
			RaceBonusEarned.Clear();
			BountyMalusAppliedBy.Clear();
			FinishRewarded.Clear();
			CurrencyBonusPercent.Clear();

			var rm = RaceManager.Instance;
			if ( rm != null )
				rm.OnPlayerFinished += HandlePlayerFinished;
		}
	}

	protected override void OnDestroy()
	{
		if ( Networking.IsHost )
		{
			var rm = RaceManager.Instance;
			if ( rm != null )
				rm.OnPlayerFinished -= HandlePlayerFinished;
		}

		if ( Instance == this ) Instance = null;
	}

	/// <summary>
	/// Idempotent: create-and-spawn a manager in the given scene if there isn't one.
	/// Safe to call from any peer; only the host actually creates the GameObject.
	/// </summary>
	public static void EnsureExists( Scene scene )
	{
		if ( !Networking.IsHost ) return;
		if ( Instance.IsValid() ) return;
		if ( scene is null ) return;

		var go = scene.CreateObject( enabled: true );
		go.Name = "PublicityCurrencyManager";
		go.Components.Create<PublicityCurrencyManager>();
	}

	// ---------- Read API (any peer) ----------

	public static int GetCurrency( ulong steamId )
	{
		if ( steamId == 0 ) return 0;
		if ( Networking.IsHost )
			return HostStore.TryGetValue( steamId, out var v ) ? v : 0;
		return ClientMirror.TryGetValue( steamId, out var c ) ? c : 0;
	}

	public static int GetCurrency( GameObject playerRoot )
		=> GetCurrency( ResolveSteamId( playerRoot ) );

	/// <summary> Steam ID of the local peer, or 0 when offline / in the editor. </summary>
	public static ulong LocalSteamId => Connection.Local?.SteamId ?? 0UL;

	/// <summary> Convenience: the local peer's current PG total. Cheap dictionary lookup. </summary>
	public static int LocalCurrency => GetCurrency( LocalSteamId );

	public static bool HasBounty( ulong steamId ) => Bounties.Contains( steamId );

	// ---------- Write API (host-only; calls on clients are no-ops) ----------

	/// <summary> Mark/unmark a player as carrying a Kopfgeld (bounty). </summary>
	public static void SetBounty( ulong steamId, bool on )
	{
		if ( !Networking.IsHost ) return;
		if ( steamId == 0 ) return;

		if ( on ) Bounties.Add( steamId );
		else      Bounties.Remove( steamId );
	}

	/// <summary>
	/// A god-ability landed a damaging hit. Callable from any peer; if called on a
	/// non-host it forwards to the host via Rpc.Host. Award +5 PG (capped) and apply
	/// bounty rules. Caller is trusted — harden if cheating becomes a concern.
	/// </summary>
	public static void NotifyAbilityHit( GameObject attackerRoot, GameObject victimRoot )
	{
		var atk = ResolveSteamId( attackerRoot );
		var vic = ResolveSteamId( victimRoot );
		if ( atk == 0 || atk == vic ) return;

		var mgr = Instance;
		if ( !mgr.IsValid() ) return;
		mgr.RpcReportAbilityHit( atk, vic );
	}

	/// <summary> A Q/E physical ram landed. Same routing as ability hits. </summary>
	public static void NotifyRamHit( GameObject attackerRoot, GameObject victimRoot )
	{
		var atk = ResolveSteamId( attackerRoot );
		var vic = ResolveSteamId( victimRoot );
		if ( atk == 0 || atk == vic ) return;

		var mgr = Instance;
		if ( !mgr.IsValid() ) return;
		mgr.RpcReportRamHit( atk, vic );
	}

	[Rpc.Host]
	private void RpcReportAbilityHit( ulong attackerSteamId, ulong victimSteamId )
	{
		if ( !Networking.IsHost ) return;
		AwardCapped( attackerSteamId, AbilityHitReward );
		ApplyBountyConsequences( attackerSteamId, victimSteamId );
	}

	[Rpc.Host]
	private void RpcReportRamHit( ulong attackerSteamId, ulong victimSteamId )
	{
		if ( !Networking.IsHost ) return;
		AwardCapped( attackerSteamId, RamHitReward );
		ApplyBountyConsequences( attackerSteamId, victimSteamId );
	}

	/// <summary>
	/// Direct host-only spend/grant for shops and altars. Returns true on success.
	/// Pass a negative amount to spend. Total never drops below 0.
	/// </summary>
	public static bool TryModify( ulong steamId, int delta )
	{
		if ( !Networking.IsHost ) return false;
		if ( steamId == 0 ) return false;

		HostStore.TryGetValue( steamId, out var cur );
		if ( cur + delta < 0 ) return false;

		AddCurrency( steamId, delta );
		return true;
	}

	/// <summary>
	/// Host-only. Set a player's PG-earn bonus (0.05 = +5%). Applied to POSITIVE rewards
	/// only — finish rewards and capped combat rewards — never to spends or the bounty
	/// malus. Set by <see cref="AltarUpgradeManager"/> at race start for Dionysos level 3.
	/// Passing 0 removes the entry.
	/// </summary>
	public static void SetCurrencyBonusPercent( ulong steamId, float pct )
	{
		if ( !Networking.IsHost ) return;
		if ( steamId == 0 ) return;

		if ( pct == 0f )
			CurrencyBonusPercent.Remove( steamId );
		else
			CurrencyBonusPercent[steamId] = pct;
	}

	// ---------- Internals ----------

	private void HandlePlayerFinished( PlayerLapTracker tracker )
	{
		if ( !Networking.IsHost || tracker is null ) return;

		var steamId = ResolveSteamId( tracker.GameObject?.Root );
		if ( steamId == 0 ) return;
		if ( !FinishRewarded.Add( steamId ) ) return;

		// Finish position == count of players already credited (1st = 1, 2nd = 2, ...).
		// Same value RaceRankingManager assigns to FinishOrder, just without the
		// subscription-order dependency.
		int position = FinishRewarded.Count;
		int reward = (position >= 1 && position < FinishRewardTable.Length)
			? FinishRewardTable[position]
			: 0;
		if ( reward <= 0 ) return;

		AddCurrency( steamId, ApplyCurrencyBonus( steamId, reward ) );
	}

	private static void AwardCapped( ulong steamId, int amount )
	{
		if ( amount <= 0 ) return;

		// Boost the combat reward by the player's PG bonus first, then cap. The +50/race
		// cap therefore counts the boosted amounts (a bonus can't be used to blow past it).
		amount = ApplyCurrencyBonus( steamId, amount );

		RaceBonusEarned.TryGetValue( steamId, out var earned );
		int remaining = BonusCapPerRace - earned;
		if ( remaining <= 0 ) return;

		int give = Math.Min( amount, remaining );
		RaceBonusEarned[steamId] = earned + give;
		AddCurrency( steamId, give );
	}

	/// <summary>
	/// Scale a positive reward by the player's <see cref="CurrencyBonusPercent"/>
	/// (Dionysos altar level-3). Rounds to nearest int. Non-positive amounts pass
	/// through unchanged so spends and the bounty malus are never inflated.
	/// </summary>
	private static int ApplyCurrencyBonus( ulong steamId, int amount )
	{
		if ( amount <= 0 ) return amount;
		if ( !CurrencyBonusPercent.TryGetValue( steamId, out var pct ) || pct == 0f )
			return amount;
		return (int)MathF.Round( amount * (1f + pct) );
	}

	private static void ApplyBountyConsequences( ulong attackerSteamId, ulong victimSteamId )
	{
		if ( victimSteamId == 0 ) return;
		if ( !Bounties.Contains( victimSteamId ) ) return;

		if ( !BountyMalusAppliedBy.TryGetValue( victimSteamId, out var set ) )
		{
			set = new HashSet<ulong>();
			BountyMalusAppliedBy[victimSteamId] = set;
		}
		if ( !set.Add( attackerSteamId ) ) return; // already paid out vs this attacker

		AddCurrency( attackerSteamId, BountyHitReward );
		AddCurrency( victimSteamId, -BountyVictimMalus );
	}

	private static void AddCurrency( ulong steamId, int delta )
	{
		if ( steamId == 0 || delta == 0 ) return;

		HostStore.TryGetValue( steamId, out var cur );
		int next = Math.Max( 0, cur + delta );
		if ( next == cur ) return;

		HostStore[steamId] = next;

		var mgr = Instance;
		if ( mgr.IsValid() )
		{
			// Broadcast also runs locally on the host, so the mirror + event fire everywhere.
			mgr.RpcSetCurrency( steamId, next );
		}
		else
		{
			ApplyMirrorLocally( steamId, next );
		}
	}

	[Rpc.Broadcast]
	private void RpcSetCurrency( ulong steamId, int amount )
	{
		ApplyMirrorLocally( steamId, amount );
	}

	private static void ApplyMirrorLocally( ulong steamId, int amount )
	{
		ClientMirror[steamId] = amount;
		OnCurrencyChanged?.Invoke( steamId, amount );
	}

	/// <summary>
	/// Steam ID of the network owner of <paramref name="root"/>. Returns 0 when the
	/// object is not network-owned (e.g. running in the editor with no lobby).
	/// </summary>
	public static ulong ResolveSteamId( GameObject root )
	{
		if ( !root.IsValid() ) return 0UL;
		var conn = root.Network?.Owner;
		if ( conn is null ) return 0UL;
		return conn.SteamId;
	}
}
