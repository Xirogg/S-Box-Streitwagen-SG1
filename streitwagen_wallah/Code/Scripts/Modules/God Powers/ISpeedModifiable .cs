using Sandbox;

/// <summary>
/// Implement on any component that should respond to powers which change movement speed
/// (e.g. Lightning slows, Mushroom boosts).
/// 
/// The power calls <see cref="SetSpeedMultiplier"/> with a value where:
///   1.0 = normal speed
///   0.5 = half speed
///   2.0 = double speed
/// 
/// Your HorseController (or whatever steers a player) implements this:
/// 
/// <code>
/// public sealed class HorseController : Component, ISpeedModifiable
/// {
///     private float speedMultiplier = 1f;
///     public void SetSpeedMultiplier( float multiplier ) => speedMultiplier = multiplier;
///     // ...use speedMultiplier in your movement code...
/// }
/// </code>
/// </summary>
public interface ISpeedModifiable
{
	void SetSpeedMultiplier( float multiplier );
}
