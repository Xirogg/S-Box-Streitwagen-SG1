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

	// ---------- Lifecycle ----------

	protected override void OnEnabled()
	{
		if ( StartOnCooldown )
			CooldownRemaining = CooldownDuration;

		if ( UltimateStartOnCooldown )
			UltimateCooldownRemaining = UltimateCooldownDuration;
	}

	protected override void OnUpdate()
	{
		// Tick cooldowns locally for everyone so UI stays in sync.
		if ( CooldownRemaining > 0f )
			CooldownRemaining = MathF.Max( 0f, CooldownRemaining - Time.Delta );

		if ( UltimateCooldownRemaining > 0f )
			UltimateCooldownRemaining = MathF.Max( 0f, UltimateCooldownRemaining - Time.Delta );

		// NOTE: For networked multiplayer, gate this with `if ( Network.IsProxy ) return;`
		// so only the owning client can press to activate.
		if ( !string.IsNullOrEmpty( ActivationAction ) && Input.Pressed( ActivationAction ) )
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
		if ( !IsReady ) return false;
		if ( !CanActivate() ) return false;

		OnActivate();
		CooldownRemaining = CooldownDuration;
		OnActivated?.Invoke();
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
		if ( !IsUltimateReady ) return false;
		if ( !CanActivateUltimate() ) return false;

		OnActivateUltimate();
		UltimateCooldownRemaining = UltimateCooldownDuration;
		OnUltimateActivated?.Invoke();
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
}
