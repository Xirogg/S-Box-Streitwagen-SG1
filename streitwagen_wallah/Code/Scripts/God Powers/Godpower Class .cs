using Sandbox;
using System;

/// <summary>
/// Base class for all god powers. Handles cooldown bookkeeping and input binding
/// for two abilities per power: a Normal ability and an Ultimate (Göttlich) ability.
/// Subclasses implement OnActivate() for the Normal effect and may override
/// OnActivateUltimate() for the Ulti effect (optional, default is no-op).
/// Attach a subclass component to a child GameObject of the PowerManager;
/// it will be auto-discovered.
/// </summary>
public abstract class GodPower : Component
{
	// ---------- Display ----------

	/// <summary>Human-readable name of the Normal ability (shown in UI).</summary>
	[Property, Group( "Display" )]
	public string DisplayName { get; set; } = "Power";

	/// <summary>Icon texture shown for the Normal ability in the UI.</summary>
	[Property, Group( "Display" )]
	public Texture Icon { get; set; }

	/// <summary>Human-readable name of the Ultimate (Göttlich) ability.</summary>
	[Property, Group( "Display" )]
	public string UltimateDisplayName { get; set; } = "Ultimate";

	/// <summary>Icon texture shown for the Ultimate ability in the UI.</summary>
	[Property, Group( "Display" )]
	public Texture UltimateIcon { get; set; }

	// ---------- Cooldown (Normal) ----------

	/// <summary>Cooldown duration in seconds after Normal activation.</summary>
	[Property, Group( "Normal Cooldown" )]
	public float CooldownDuration { get; set; } = 10f;

	/// <summary>If true, the Normal ability starts on cooldown when enabled.</summary>
	[Property, Group( "Normal Cooldown" )]
	public bool StartOnCooldown { get; set; } = false;

	// ---------- Cooldown (Ultimate) ----------

	/// <summary>Cooldown duration in seconds after Ultimate activation.</summary>
	[Property, Group( "Ultimate Cooldown" )]
	public float UltimateCooldownDuration { get; set; } = 30f;

	/// <summary>If true, the Ultimate ability starts on cooldown when enabled.</summary>
	[Property, Group( "Ultimate Cooldown" )]
	public bool UltimateStartOnCooldown { get; set; } = false;

	// ---------- Input ----------

	/// <summary>
	/// Input action name as defined in Project Settings → Input.
	/// Pressing this action activates the Normal ability (when modifier is NOT held)
	/// or the Ultimate ability (when modifier IS held).
	/// Leave empty to disable hotkey activation (then trigger via TryActivate / TryActivateUltimate).
	/// </summary>
	[Property, Group( "Input" )]
	public string ActivationAction { get; set; } = "PowerActivate";

	/// <summary>
	/// Input action that, when held during ActivationAction press, triggers the Ultimate ability.
	/// Leave empty to disable hotkey-based Ultimate activation.
	/// </summary>
	[Property, Group( "Input" )]
	public string UltimateModifierAction { get; set; } = "Run";

	/// <summary>
	/// If true, the power can also be triggered by its own ActivationAction hotkey
	/// (legacy behaviour). For item-only powers leave this false so the ONLY way to
	/// fire it is through PlayerItemTracker after picking up an item.
	/// </summary>
	[Property, Group( "Input" )]
	public bool AllowDirectInput { get; set; } = false;

	// ---------- Single Use (Item) ----------

	/// <summary>
	/// If true, the power is consumed after a single activation (Normal OR Ultimate)
	/// and cannot be used again until Rearm() is called (the item system does this on
	/// </summary>
	[Property, Group( "Single Use" )]
	public bool SingleUse { get; set; } = true;

	/// <summary>
	/// If true, the power begins life already spent, so it can't be activated until it
	/// is granted via the item system. Leave true for item-only powers.
	/// </summary>
	[Property, Group( "Single Use" )]
	public bool StartSpent { get; set; } = true;

	// ---------- UI ----------

	/// <summary>
	/// Cooldown indicator prefab spawned for this power.
	/// The PowerManager instantiates it and binds it to this power.
	/// </summary>
	[Property, Group( "UI" )]
	public GameObject IndicatorPrefab { get; set; }

	// ---------- Runtime State (Normal) ----------

	/// <summary>Seconds remaining on the Normal cooldown (0 = ready).</summary>
	public float CooldownRemaining { get; private set; }

	/// <summary>0 = just activated, 1 = ready. Useful for radial fill UI.</summary>
	public float CooldownProgress =>
		CooldownDuration <= 0f ? 1f : 1f - MathX.Clamp( CooldownRemaining / CooldownDuration, 0f, 1f );

	/// <summary>True if the Normal ability is off cooldown.</summary>
	public bool IsReady => CooldownRemaining <= 0f;

	// ---------- Runtime State (Ultimate) ----------

	/// <summary>Seconds remaining on the Ultimate cooldown (0 = ready).</summary>
	public float UltimateCooldownRemaining { get; private set; }

	/// <summary>0 = just activated, 1 = ready. Useful for radial fill UI.</summary>
	public float UltimateCooldownProgress =>
		UltimateCooldownDuration <= 0f ? 1f : 1f - MathX.Clamp( UltimateCooldownRemaining / UltimateCooldownDuration, 0f, 1f );

	/// <summary>True if the Ultimate ability is off cooldown.</summary>
	public bool IsUltimateReady => UltimateCooldownRemaining <= 0f;

	// ---------- Runtime State (Single Use) ----------

	/// <summary>
	/// True once the power has been used (and SingleUse is on). Blocks any further
	/// activation until Rearm() is called.
	/// </summary>
	public bool IsSpent { get; private set; }

	// ---------- Owner & Events ----------

	/// <summary>
	/// The player/agent that owns this power. Set by the PowerManager,
	/// or assign manually if you spawn powers another way.
	/// </summary>
	public GameObject Owner { get; set; }

	/// <summary>Fires after a successful Normal activation (after OnActivate returns).</summary>
	public event Action OnActivated;

	/// <summary>Fires after a successful Ultimate activation (after OnActivateUltimate returns).</summary>
	public event Action OnUltimateActivated;

	/// <summary>
	/// Fires when the power is consumed (single-use). The item system listens to this
	/// to clear the player's held item and update the UI. The component is intentionally
	/// NOT disabled here, so any timed after-effects still finish.
	/// </summary>
	public event Action<GodPower> OnConsumed;

	// ---------- Lifecycle ----------

	protected override void OnEnabled()
	{
		if ( StartOnCooldown )
			CooldownRemaining = CooldownDuration;

		if ( UltimateStartOnCooldown )
			UltimateCooldownRemaining = UltimateCooldownDuration;

		// Item-only powers begin spent: they can't be fired until granted via an item.
		if ( StartSpent )
			IsSpent = true;
	}

	protected override void OnUpdate()
	{
		// Tick cooldowns locally for everyone so UI stays in sync.
		if ( CooldownRemaining > 0f )
			CooldownRemaining = MathF.Max( 0f, CooldownRemaining - Time.Delta );

		if ( UltimateCooldownRemaining > 0f )
			UltimateCooldownRemaining = MathF.Max( 0f, UltimateCooldownRemaining - Time.Delta );

		// Legacy hotkey path. Off by default for item-only powers (AllowDirectInput=false)
		// and always blocked once the power is spent.
		// NOTE: For networked multiplayer, gate this with `if ( Network.IsProxy ) return;`
		// so only the owning client can press to activate.
		if ( AllowDirectInput && !IsSpent
			&& !string.IsNullOrEmpty( ActivationAction ) && Input.Pressed( ActivationAction ) )
		{
			bool modifierHeld = !string.IsNullOrEmpty( UltimateModifierAction )
				&& Input.Down( UltimateModifierAction );

			if ( modifierHeld )
				TryActivateUltimate();
			else
				TryActivate();
		}
	}

	// ---------- Activation (Normal) ----------

	/// <summary>Tries to activate the Normal ability. Returns true on success.</summary>
	public bool TryActivate()
	{
		if ( IsSpent ) return false;
		if ( !IsReady ) return false;
		if ( !CanActivate() ) return false;

		OnActivate();
		CooldownRemaining = CooldownDuration;
		OnActivated?.Invoke();

		if ( SingleUse )
			MarkSpent();

		return true;
	}

	/// <summary>
	/// Override for extra gating on the Normal ability (e.g. an effect is already running).
	/// Default: allows activation whenever off cooldown.
	/// </summary>
	protected virtual bool CanActivate() => true;

	/// <summary>Implement the Normal effect of the power.</summary>
	protected abstract void OnActivate();

	// ---------- Activation (Ultimate) ----------

	/// <summary>Tries to activate the Ultimate ability. Returns true on success.</summary>
	public bool TryActivateUltimate()
	{
		if ( IsSpent ) return false;
		if ( !IsUltimateReady ) return false;
		if ( !CanActivateUltimate() ) return false;

		OnActivateUltimate();
		UltimateCooldownRemaining = UltimateCooldownDuration;
		OnUltimateActivated?.Invoke();

		if ( SingleUse )
			MarkSpent();

		return true;
	}

	/// <summary>
	/// Override for extra gating on the Ultimate ability.
	/// Default: allows activation whenever off cooldown.
	/// </summary>
	protected virtual bool CanActivateUltimate() => true;

	/// <summary>
	/// Implement the Ultimate (Göttlich) effect of the power.
	/// Optional — default is no-op so existing single-ability subclasses keep working.
	/// </summary>
	protected virtual void OnActivateUltimate() { }

	// ---------- Single-Use Helpers ----------

	/// <summary>
	/// Marks the power as spent and notifies listeners (the item system clears the held
	/// item + UI). Intentionally does NOT disable or destroy the component, so timed
	/// after-effects that revert via Invoke (e.g. Dionysos drunk, Ma'at judgement) still
	/// finish cleanly. The spent flag + the cleared item are what make it "disappear".
	/// </summary>
	public void MarkSpent()
	{
		if ( IsSpent ) return;
		IsSpent = true;
		OnConsumed?.Invoke( this );
	}

	/// <summary>
	/// Re-arms the power so it can be used once more. Called by the item system when a
	/// new item is granted. Clears the spent flag and both cooldowns so the freshly
	/// picked-up item is immediately usable.
	/// </summary>
	public void Rearm()
	{
		IsSpent = false;
		CooldownRemaining = 0f;
		UltimateCooldownRemaining = 0f;
	}
}
