using Sandbox;
using System;

/// <summary>
/// Intermediary between God Power scripts and the HUD. One instance lives on
/// the player root (auto-created on first use by <see cref="GodPower.ResolveNotifier"/>),
/// so it survives the short-lived GodPower clones that spawn, fire, and self-destroy.
///
/// God Powers push a transient status string via <see cref="Show"/>,
/// <see cref="ShowTimed"/>, or <see cref="ShowDynamic"/>; the Placeholder Panem UI
/// razor reads <see cref="CurrentText"/> each frame and renders it top-center.
///
/// Multiplayer: state is plain (not [Sync]). Power clones only exist on the
/// owning client, and the HUD razor renders only when `!IsProxy`, so each player
/// sees their own messages and nothing else.
/// </summary>
public sealed class GodPowerNotifier : Component
{
	/// <summary>Default lifetime for plain <see cref="Show"/> calls with no timer.</summary>
	private const float DefaultDuration = 5f;

	private string baseText;
	private float endsAt;
	private bool showCountdown;
	private Func<float> remainingProvider;

	/// <summary>Final text the UI renders. Empty string when nothing is active.</summary>
	public string CurrentText { get; private set; } = "";

	/// <summary>Show <paramref name="text"/> for 5 seconds. No countdown appended.</summary>
	public void Show( string text )
	{
		baseText = text;
		endsAt = Time.Now + DefaultDuration;
		showCountdown = false;
		remainingProvider = null;
		RefreshText();
	}

	/// <summary>
	/// Show <paramref name="text"/> tied to a known fixed duration. A countdown
	/// is appended each frame; the message clears when the timer hits zero.
	/// </summary>
	public void ShowTimed( string text, float duration )
	{
		baseText = text;
		endsAt = Time.Now + duration;
		showCountdown = true;
		remainingProvider = null;
		RefreshText();
	}

	/// <summary>
	/// Show <paramref name="text"/> driven by an external clock — used when the
	/// effect can end early (e.g. Ma'at's shield being consumed). The message
	/// clears as soon as <paramref name="remainingFunc"/> returns &lt;= 0.
	/// </summary>
	public void ShowDynamic( string text, Func<float> remainingFunc, bool withCountdown = true )
	{
		baseText = text;
		remainingProvider = remainingFunc;
		showCountdown = withCountdown;
		endsAt = 0f;
		RefreshText();
	}

	/// <summary>Hide the current message immediately.</summary>
	public void Clear()
	{
		baseText = null;
		remainingProvider = null;
		CurrentText = "";
	}

	protected override void OnUpdate() => RefreshText();

	private void RefreshText()
	{
		if ( baseText is null )
		{
			CurrentText = "";
			return;
		}

		float remaining = remainingProvider is not null
			? remainingProvider.Invoke()
			: endsAt - Time.Now;

		if ( remaining <= 0f )
		{
			Clear();
			return;
		}

		CurrentText = showCountdown
			? $"{baseText} ({MathF.Ceiling( remaining ):0}s)"
			: baseText;
	}
}
