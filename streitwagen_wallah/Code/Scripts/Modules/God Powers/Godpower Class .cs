using Sandbox;
using System;

/// <summary>
/// Base class for all god powers. Handles cooldown bookkeeping and input binding.
/// Subclasses implement OnActivate() for the actual effect.
/// Attach a subclass component to a child GameObject of the PowerManager;
/// it will be auto-discovered.
/// </summary>
public abstract class GodPower : Component
{
	// ---------- Display ----------

	/// <summary>Human-readable name of the power (shown in UI).</summary>
	[Property, Group( "Display" )]
	public string DisplayName { get; set; } = "Power";

	/// <summary>Icon texture shown for this power in the UI.</summary>
	[Property, Group( "Display" )]
	public Texture Icon { get; set; }

	// ---------- Cooldown ----------

	/// <summary>Cooldown duration in seconds after activation.</summary>
	[Property, Group( "Cooldown" )]
	public float CooldownDuration { get; set; } = 10f;

	/// <summary>If true, the power starts on cooldown when enabled.</summary>
	[Property, Group( "Cooldown" )]
	public bool StartOnCooldown { get; set; } = false;

	// ---------- Input ----------

	/// <summary>
	/// Input action name as defined in Project Settings → Input.
	/// Pressing this action activates the power.
	/// Leave empty to disable hotkey activation (then trigger via TryActivate()).
	/// </summary>
	[Property, Group( "Input" )]
	public string ActivationAction { get; set; } = "PowerActivate";

	// ---------- UI ----------

	/// <summary>
	/// Cooldown indicator prefab spawned for this power.
	/// The PowerManager instantiates it and binds it to this power.
	/// </summary>
	[Property, Group( "UI" )]
	public GameObject IndicatorPrefab { get; set; }

	// ---------- Runtime State ----------

	/// <summary>Seconds remaining on the cooldown (0 = ready).</summary>
	public float CooldownRemaining { get; private set; }

	/// <summary>0 = just activated, 1 = ready. Useful for radial fill UI.</summary>
	public float CooldownProgress =>
		CooldownDuration <= 0f ? 1f : 1f - MathX.Clamp( CooldownRemaining / CooldownDuration, 0f, 1f );

	/// <summary>True if the power is off cooldown and CanActivate() passes.</summary>
	public bool IsReady => CooldownRemaining <= 0f;

	/// <summary>
	/// The player/agent that owns this power. Set by the PowerManager,
	/// or assign manually if you spawn powers another way.
	/// </summary>
	public GameObject Owner { get; set; }

	/// <summary>Fires after a successful activation (after OnActivate returns).</summary>
	public event Action OnActivated;

	// ---------- Lifecycle ----------

	protected override void OnEnabled()
	{
		if ( StartOnCooldown )
			CooldownRemaining = CooldownDuration;
	}

	protected override void OnUpdate()
	{
		// Tick cooldown locally for everyone so UI stays in sync.
		if ( CooldownRemaining > 0f )
			CooldownRemaining = MathF.Max( 0f, CooldownRemaining - Time.Delta );

		// NOTE: For networked multiplayer, gate this with `if ( Network.IsProxy ) return;`
		// so only the owning client can press to activate.
		if ( !string.IsNullOrEmpty( ActivationAction ) && Input.Pressed( ActivationAction ) )
		{
			TryActivate();
		}
	}

	// ---------- Activation ----------

	/// <summary>Tries to activate the power. Returns true on success.</summary>
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
	/// Override for extra gating (e.g. an effect is already running).
	/// Default: allows activation whenever off cooldown.
	/// </summary>
	protected virtual bool CanActivate() => true;

	/// <summary>Implement the actual effect of the power.</summary>
	protected abstract void OnActivate();
}
