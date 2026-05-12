using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Lightning: all players (except the caster, by default) shrink and move slower
/// for a duration. Inspired by the Mario Kart lightning bolt item.
/// </summary>
public sealed class LightningPower : GodPower
{
	[Property, Group( "Lightning Settings" )]
	public float ShrinkScale { get; set; } = 0.5f;

	[Property, Group( "Lightning Settings" ), Range( 0f, 1f )]
	public float SpeedReduction { get; set; } = 0.5f;

	[Property, Group( "Lightning Settings" )]
	public float EffectDuration { get; set; } = 5f;

	/// <summary>Tag used to find affected players in the scene.</summary>
	[Property, Group( "Lightning Settings" )]
	public string PlayerTag { get; set; } = "player";

	/// <summary>If true, the caster is also affected. Default false (Mario Kart style).</summary>
	[Property, Group( "Lightning Settings" )]
	public bool AffectOwner { get; set; } = false;

	private const string SpeedKey = "lightning";

	private bool effectActive = false;

	private struct AffectedPlayer
	{
		public GameObject GameObject;
		public Vector3 OriginalScale;
		public ISpeedModifiable SpeedTarget;
	}

	private readonly List<AffectedPlayer> affectedPlayers = new();

	protected override bool CanActivate() => !effectActive;

	protected override void OnActivate()
	{
		effectActive = true;
		affectedPlayers.Clear();

		var players = Scene.FindAllWithTag( PlayerTag );

		foreach ( var player in players )
		{
			if ( !AffectOwner && Owner.IsValid() && player == Owner )
				continue;

			var ap = new AffectedPlayer
			{
				GameObject = player,
				OriginalScale = player.LocalScale,
				SpeedTarget = player.Components.Get<ISpeedModifiable>( FindMode.EverythingInSelfAndDescendants )
			};

			player.LocalScale = ap.OriginalScale * ShrinkScale;
			ap.SpeedTarget?.SetSpeedMultiplier( SpeedKey, SpeedReduction );

			affectedPlayers.Add( ap );
		}

		//Log.Info( $"[LightningPower] {affectedPlayers.Count} players affected for {EffectDuration}s" );

		// s&box equivalent of Unity's WaitForSeconds — schedules a callback.
		Invoke( EffectDuration, RevertEffect );
	}

	private void RevertEffect()
	{
		foreach ( var ap in affectedPlayers )
		{
			if ( ap.GameObject.IsValid() )
				ap.GameObject.LocalScale = ap.OriginalScale;

			ap.SpeedTarget?.ClearSpeedMultiplier( SpeedKey );
		}

		affectedPlayers.Clear();
		effectActive = false;
	}

	protected override void OnDisabled()
	{
		// Safety: if the component is disabled mid-effect, undo the changes
		// so players don't get stuck shrunken/slowed forever.
		if ( effectActive )
			RevertEffect();
	}
}
