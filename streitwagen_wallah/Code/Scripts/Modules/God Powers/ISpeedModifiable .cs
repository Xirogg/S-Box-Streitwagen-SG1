using Sandbox;

/// <summary>
/// Implement on any component that should respond to powers/effects which
/// change movement speed (e.g. Lightning slows, Mushroom boosts, damage penalty).
///
/// Modifiers are stacked by key — the implementer multiplies all active
/// entries together to get the effective speed multiplier. A multiplier of:
///   1.0 = no effect
///   0.5 = half speed
///   2.0 = double speed
///   0.0 = stopped
///
/// Each effect should pick a unique key (e.g. "lightning", "damage") and
/// call <see cref="ClearSpeedMultiplier"/> when it ends.
/// </summary>
public interface ISpeedModifiable
{
	void SetSpeedMultiplier( string key, float multiplier );
	void ClearSpeedMultiplier( string key );
}
