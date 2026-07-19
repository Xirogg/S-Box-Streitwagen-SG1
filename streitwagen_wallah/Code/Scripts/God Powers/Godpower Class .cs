using Sandbox;
using System;

/// <summary>
/// Base class for all god powers under the new item-driven architecture.
///
/// Lifecycle:
///   1. Spawned as a clone of a GodPower-prefab by PlayerItemTracker on pickup. At
///      that moment the pickup rolls a chance to make this the Ultimate version and
///      sets <see cref="IsUltimate"/> accordingly — the player no longer chooses.
///   2. Tracker calls TryActivate() or TryActivateUltimate() exactly once when the
///      player presses Use. Which one runs is decided purely by IsUltimate.
///   3. After activation the component lives for ActiveLingerDuration seconds so any
///      timed after-effects (drunk, judgement) finish, then it is destroyed.
///
/// There are no cooldowns anymore — the "you have one shot" gate is IsSpent, which
/// flips true after the first successful activation.
/// </summary>
public abstract class GodPower : Component
{
	// ---------- Display ----------

	[Property, Group( "Display" )]
	public string DisplayName { get; set; } = "Power";

	[Property, Group( "Display" )]
	public Texture Icon { get; set; }

	[Property, Group( "Display" )]
	public string UltimateDisplayName { get; set; } = "Ultimate";

	[Property, Group( "Display" )]
	public Texture UltimateIcon { get; set; }

	// ---------- Linger ----------

	/// <summary>
	/// Seconds the spawned GameObject must stay alive AFTER a successful Normal
	/// activation, so timed after-effects can run to completion. PlayerItemTracker
	/// reads this and destroys the instance when the timer elapses. 0 = destroy
	/// immediately.
	/// </summary>
	[Property, Group( "Linger" )]
	public float LingerAfterNormal { get; set; } = 0f;

	/// <summary>
	/// Seconds the spawned GameObject must stay alive after a successful Ultimate
	/// activation. Set this to at least the longest revert delay your Ultimate
	/// schedules (Dionysos drunk = 8s, Ma'at judgement = 6s).
	/// </summary>
	[Property, Group( "Linger" )]
	public float LingerAfterUltimate { get; set; } = 0f;

	// ---------- Runtime ----------

	private GameObject _owner;

	/// <summary>
	/// The player root this power belongs to. Assigned by PlayerItemTracker right
	/// after cloning. Subclasses use this to find the player's horse, damage system,
	/// etc.
	/// </summary>
	public GameObject Owner
	{
		get => _owner;
		set
		{
			if ( _owner == value ) return;
			_owner = value;
			OnOwnerAssigned();
		}
	}

	/// <summary>
	/// Called after <see cref="Owner"/> is set. Override to do one-time per-player
	/// setup like resolving references to nodes in the player's chariot prefab.
	/// </summary>
	protected virtual void OnOwnerAssigned() { }

	/// <summary>True once the power has been used. Blocks any further activation.</summary>
	public bool IsSpent { get; private set; }

	/// <summary>
	/// Which variant this pickup rolled. Set once by PlayerItemTracker right after the
	/// clone is spawned (15% chance on a normal item-box pickup, or inherited from the
	/// stolen item on a Laverna transfer). When true the player's Use press fires the
	/// Ultimate ability; when false it fires the Normal one. The player no longer picks.
	/// </summary>
	public bool IsUltimate { get; set; }

	/// <summary>
	/// The linger value picked at activation time (LingerAfterNormal or
	/// LingerAfterUltimate). PlayerItemTracker reads this immediately after a
	/// successful TryActivate* call to schedule destruction.
	/// </summary>
	public float ActiveLingerDuration { get; private set; }

	// ---------- Events ----------

	public event Action OnActivated;
	public event Action OnUltimateActivated;

	/// <summary>
	/// Fires when the power is marked spent (after a successful activation). The
	/// item system uses this if it wants to react before the linger destroy.
	/// </summary>
	public event Action<GodPower> OnConsumed;

	// ---------- Activation ----------

	public bool TryActivate()
	{
		if ( IsSpent ) return false;
		if ( !CanActivate() ) return false;

		OnActivate();
		OnActivated?.Invoke();
		ActiveLingerDuration = LingerAfterNormal;
		MarkSpent();
		return true;
	}

	public bool TryActivateUltimate()
	{
		if ( IsSpent ) return false;
		if ( !CanActivateUltimate() ) return false;

		OnActivateUltimate();
		OnUltimateActivated?.Invoke();
		ActiveLingerDuration = LingerAfterUltimate;
		MarkSpent();
		return true;
	}

	/// <summary>Override for extra gating on the Normal ability. Default: always allowed.</summary>
	protected virtual bool CanActivate() => true;

	/// <summary>Override for extra gating on the Ultimate ability. Default: always allowed.</summary>
	protected virtual bool CanActivateUltimate() => true;

	/// <summary>Implement the Normal effect of the power.</summary>
	protected abstract void OnActivate();

	/// <summary>
	/// Implement the Ultimate effect of the power. Default no-op so subclasses without
	/// an Ultimate keep working.
	/// </summary>
	protected virtual void OnActivateUltimate() { }

	private void MarkSpent()
	{
		if ( IsSpent ) return;
		IsSpent = true;
		OnConsumed?.Invoke( this );
	}

	// ---------- SFX ----------

	/// <summary>
	/// Find the casting player's <see cref="GodPowersNormalSfxmodule"/> (one lives on
	/// each player prefab). Powers route their NORMAL sounds through it so the audio
	/// outlives this short-lived clone. Returns null if Owner isn't assigned yet.
	/// </summary>
	protected GodPowersNormalSfxmodule ResolveNormalSfx()
	{
		if ( !Owner.IsValid() ) return null;
		return Owner.Components.Get<GodPowersNormalSfxmodule>( FindMode.EverythingInSelfAndDescendants );
	}
}
