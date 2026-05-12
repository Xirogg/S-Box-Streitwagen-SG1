using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages all <see cref="GodPower"/> components attached to this GameObject or its children.
/// On start, it discovers powers, assigns their Owner, and optionally spawns cooldown
/// indicator UI prefabs that get bound to each power.
/// </summary>
public sealed class PowerManager : Component
{
	/// <summary>
	/// The player/agent that owns these powers.
	/// Defaults to this GameObject's parent (or self if no parent) when unset.
	/// </summary>
	[Property]
	public GameObject Owner { get; set; }

	/// <summary>
	/// If set, indicator prefabs are parented under this object (typically a UI panel).
	/// Otherwise they're spawned at the scene root.
	/// </summary>
	[Property]
	public GameObject IndicatorRoot { get; set; }

	/// <summary>All powers currently registered with this manager (read-only).</summary>
	public IReadOnlyList<GodPower> Powers => powers;

	private readonly List<GodPower> powers = new();
	private readonly List<GameObject> indicators = new();

	protected override void OnStart()
	{
		// Default owner: parent of the manager, or self if no parent.
		if ( !Owner.IsValid() )
			Owner = GameObject.Parent ?? GameObject;

		// Discover all GodPower components on self and descendants.
		var found = Components.GetAll<GodPower>( FindMode.EverythingInSelfAndDescendants );
		powers.Clear();
		powers.AddRange( found );

		foreach ( var power in powers )
		{
			power.Owner = Owner;
			SpawnIndicator( power );
		}

		//Log.Info( $"[PowerManager] Registered {powers.Count} powers for {Owner.Name}" );
	}

	private void SpawnIndicator( GodPower power )
	{
		if ( !power.IndicatorPrefab.IsValid() ) return;

		var clone = power.IndicatorPrefab.Clone();

		if ( IndicatorRoot.IsValid() )
			clone.SetParent( IndicatorRoot, false );

		// If the indicator prefab carries a component implementing IPowerIndicator,
		// wire it up to this power so it can read CooldownProgress / Icon / etc.
		var indicator = clone.Components.Get<IPowerIndicator>( FindMode.EverythingInSelfAndDescendants );
		indicator?.Bind( power );

		indicators.Add( clone );
	}

	protected override void OnDestroy()
	{
		foreach ( var ind in indicators )
		{
			if ( ind.IsValid() )
				ind.Destroy();
		}
		indicators.Clear();
	}

	/// <summary>Find a registered power by type. Returns null if none.</summary>
	public T GetPower<T>() where T : GodPower => powers.OfType<T>().FirstOrDefault();
}

/// <summary>
/// Implement on the cooldown UI component so the PowerManager can hand it
/// the GodPower it should display.
/// </summary>
public interface IPowerIndicator
{
	void Bind( GodPower power );
}
