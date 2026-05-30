using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Legacy helper. Under the new item-driven architecture, GodPower components are
/// no longer pre-attached to the player — they're cloned in by PlayerItemTracker
/// when an item is picked up, and destroyed after use. The cooldown-indicator
/// system this manager used to drive is replaced by the HUD label in
/// Placeholder Panem Component.razor.
///
/// You can safely remove this component from the player prefab. It's kept here as
/// a minimal no-op so existing prefab references don't break — and it still does
/// the one useful thing that survives: at OnStart it walks its subtree and assigns
/// Owner to any GodPower components it happens to find. PlayerItemTracker does the
/// same job when it spawns a power, so this is only relevant for powers that exist
/// at scene load (typically none in the new model).
/// </summary>
public sealed class PowerManager : Component
{
	/// <summary>
	/// The player/agent that owns these powers. Defaults to this GameObject's parent
	/// (or self if no parent) when unset.
	/// </summary>
	[Property]
	public GameObject Owner { get; set; }

	/// <summary>All powers currently registered with this manager (read-only).</summary>
	public IReadOnlyList<GodPower> Powers => powers;

	private readonly List<GodPower> powers = new();

	protected override void OnStart()
	{
		if ( !Owner.IsValid() )
			Owner = GameObject.Parent ?? GameObject;

		// Discover any GodPower components present at scene-load time and assign
		// their Owner. In the new flow this list is normally empty because powers
		// are spawned at runtime by PlayerItemTracker.
		var found = Components.GetAll<GodPower>( FindMode.EverythingInSelfAndDescendants );
		powers.Clear();
		powers.AddRange( found );

		foreach ( var power in powers )
			power.Owner = Owner;
	}

	/// <summary>Find a registered power by type. Returns null if none.</summary>
	public T GetPower<T>() where T : GodPower => powers.OfType<T>().FirstOrDefault();
}
