using Sandbox;
using System;

/// <summary>
/// Passiver Karma-Schild von Ma'at. Wird vom MaatPower aktiviert und liegt
/// auf dem Spieler-Prefab. Andere Powers fragen via TryConsume() ab, ob sie
/// reflektiert werden müssen. Trauben-Projektile werden direkt hier per
/// Kollision behandelt (Velocity invertieren — kein Auto-Aim).
/// </summary>
public sealed class MaatKarmaShield : Component, Component.ICollisionListener
{
	[Property, Group( "Reflection" )]
	public string GrapeTag { get; set; } = "grape";

	// --- Visual (semi-transparent dome around the player while the shield is up) ---
	// Defaults to the engine's built-in unit sphere so there IS a visual out of the box.
	// Drop a translucent material into ShieldMaterial to actually see through it — the
	// default sphere material is opaque, so the tint alpha alone won't cut it.
	[Property, Group( "Visual" )]
	public Model ShieldModel { get; set; } = Model.Sphere;

	[Property, Group( "Visual" )]
	public Material ShieldMaterial { get; set; }

	[Property, Group( "Visual" )]
	public Color ShieldColor { get; set; } = Color.Cyan.WithAlpha( 0.35f );

	/// <summary>World-units radius of the dome (the built-in sphere is a unit sphere).</summary>
	[Property, Group( "Visual" )]
	public float ShieldRadius { get; set; } = 80f;

	/// <summary>
	/// Local-space offset of the dome from its origin node. Defaults to z +1 so the
	/// visual sits slightly above the node instead of dead-centre on it.
	/// </summary>
	[Property, Group( "Visual" )]
	public Vector3 ShieldOffset { get; set; } = Vector3.Up;

	public bool IsActive { get; private set; }

	public event Action OnConsumed;

	// Child object that carries the shield mesh. Created lazily, toggled on IsActive edges.
	private GameObject _visualObject;
	private ModelRenderer _visualRenderer;
	private bool _visualShown;
	private bool _warnedNoModel;

	/// <summary>
	/// Aktiviert den Schild. Er läuft bewusst NICHT per Timer ab — er bleibt aktiv,
	/// bis ihn eine gegnerische Fähigkeit über <see cref="TryConsume"/> verbraucht
	/// (Ma'at-Ult, Laverna-Ult oder eine Traube). Es gibt daher keine Restlaufzeit.
	/// </summary>
	public void Activate()
	{
		IsActive = true;
		PlayShieldSound(); // Sound A — shield raised (proximity)
	}

	/// <summary>True und konsumiert den Schild, wenn aktiv. Sonst false.</summary>
	public bool TryConsume()
	{
		if ( !IsActive ) return false;
		IsActive = false;
		PlayShieldSound(); // Sound A — shield destroyed/reflected (proximity)
		OnConsumed?.Invoke();
		return true;
	}

	/// <summary>
	/// Route Ma'at's normal sound through the player's persistent SFX module. The shield
	/// itself isn't erased like the GodPower clone, so it can safely own this call.
	/// </summary>
	private void PlayShieldSound()
	{
		var root = GameObject?.Root;
		var sfx = root?.Components.Get<GodPowersNormalSfxmodule>( FindMode.EverythingInSelfAndDescendants );
		sfx?.PlayMaatShield();
	}

	protected override void OnUpdate()
	{
		// Kein Timer mehr: der Schild bleibt aktiv, bis TryConsume() ihn verbraucht.
		// Nur den Renderer umschalten, wenn IsActive kippt — kein Per-Frame-Thrash.
		if ( IsActive != _visualShown )
			SetVisual( IsActive );
	}

	/// <summary>Show/hide the dome, creating it the first time it's needed.</summary>
	private void SetVisual( bool show )
	{
		_visualShown = show;

		if ( show )
			EnsureVisual();

		if ( _visualRenderer.IsValid() )
			_visualRenderer.Enabled = show;
	}

	/// <summary>Lazily build the child GameObject + ModelRenderer that draws the dome.</summary>
	private void EnsureVisual()
	{
		if ( _visualRenderer.IsValid() ) return;

		if ( ShieldModel is null )
		{
			if ( !_warnedNoModel )
			{
				Log.Warning( "[MaatKarmaShield] ShieldModel nicht gesetzt — Schild bleibt unsichtbar." );
				_warnedNoModel = true;
			}
			return;
		}

		_visualObject = Scene.CreateObject();
		_visualObject.Name = "MaatShieldVisual";
		_visualObject.SetParent( GameObject, false );
		_visualObject.LocalPosition = ShieldOffset;
		_visualObject.LocalScale = ShieldRadius; // uniform: unit sphere → radius world units

		_visualRenderer = _visualObject.Components.Create<ModelRenderer>();
		_visualRenderer.Model = ShieldModel;
		_visualRenderer.Tint = ShieldColor;
		if ( ShieldMaterial is not null )
			_visualRenderer.MaterialOverride = ShieldMaterial;
	}

	public void OnCollisionStart( Collision o )
	{
		if ( !IsActive ) return;

		var otherRoot = o.Other.GameObject?.Root;
		if ( otherRoot is null || otherRoot == GameObject.Root ) return;
		if ( !otherRoot.Tags.Has( GrapeTag ) ) return;

		var rb = o.Other.GameObject.Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		if ( rb is not null )
			rb.Velocity = -rb.Velocity;

		TryConsume();
	}

	public void OnCollisionUpdate( Collision o ) { }
	public void OnCollisionStop( CollisionStop o ) { }

	protected override void OnDestroy()
	{
		if ( _visualObject.IsValid() )
			_visualObject.Destroy();
	}
}
