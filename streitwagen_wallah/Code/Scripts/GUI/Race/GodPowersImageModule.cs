using Sandbox;

namespace Sandbox;

/// <summary>
/// Scene-wide module that pops a god-power's ULTIMATE image up in the sky.
///
/// This is the visual twin of <see cref="GodPowersUltimateSfxmodule"/> and is meant to
/// live on the SAME networked scene object. Each god's <c>OnActivateUltimate()</c> fires
/// its ult sound through the SFX module and, right next to it, its image through this one:
///   <c>GodPowersImageModule.Instance?.ShowTaranisImage();</c>
///
/// Per-player, aimed at each player:
///   The casting client broadcasts (like the SFX module), and EVERY client then spawns its
///   OWN local copy of the image, positioned relative to ITS OWN chariot. So all players see
///   the same picture, but each sees it hanging in the sky in front of their own chariot —
///   <see cref="Distance"/> units ahead (along the chariot's facing), <see cref="Height"/>
///   units up, scaled to <see cref="ScalePercent"/>%. The image billboards toward the
///   camera (WorldPanel.LookAtCamera), so it always faces the viewer.
///
/// The spawned image is a plain, un-networked GameObject (never NetworkSpawn'd): a
/// <see cref="Sandbox.WorldPanel"/> renderer + a <see cref="GodPowerSkyImage"/> razor that
/// draws the texture and self-destructs after <see cref="Duration"/> seconds.
///
/// Setup:
///   - Put exactly ONE of these on a networked scene object (same one as the SFX module).
///   - Drag one image Texture into each per-god slot below.
///   - Tune Distance / Height / ScalePercent / Duration to taste.
///
/// Networking: with no active session (editor / single-player) the calls just spawn locally.
/// </summary>
public sealed class GodPowersImageModule : Component
{
	/// <summary>Stable ids sent over the RPC so no Texture travels the wire.</summary>
	public enum GodImage
	{
		Taranis, Maat, Dionysos, Laverna,
	}

	[Property, Group( "Images" )] public Texture TaranisImage { get; set; }
	[Property, Group( "Images" )] public Texture MaatImage { get; set; }
	[Property, Group( "Images" )] public Texture DionysosImage { get; set; }
	[Property, Group( "Images" )] public Texture LavernaImage { get; set; }

	/// <summary>How far in front of the chariot (along its facing) the image hangs, in world units.
	/// Negative puts it behind the chariot if a given god's forward reads the other way.</summary>
	[Property, Group( "Placement" )] public float Distance { get; set; } = 1000f;

	/// <summary>How much higher than the chariot the image sits, in world units (straight up).</summary>
	[Property, Group( "Placement" )] public float Height { get; set; } = 500f;

	/// <summary>Image scale, in percent. 500 = 500% = 5×.</summary>
	[Property, Group( "Placement" ), Range( 10f, 2000f )]
	public float ScalePercent { get; set; } = 500f;

	/// <summary>How long the image stays up before it fades out and removes itself, in seconds.</summary>
	[Property, Group( "Placement" )] public float Duration { get; set; } = 5f;

	[Property, Group( "Debug" )]
	public bool DebugLog { get; set; } = false;

	/// <summary>Provisional test: when enabled, fires the Ma'at image once, this many seconds
	/// after the map loads, so you can see the placement without triggering a real ultimate.</summary>
	[Property, Group( "Debug" )]
	public bool DebugSpawnMaatOnStart { get; set; } = false;

	[Property, Group( "Debug" )]
	public float DebugSpawnDelay { get; set; } = 5f;

	/// <summary>Base panel height in panel-pixels; width is derived from the image's aspect ratio.
	/// The panel's WORLD size is this base × the object's WorldScale (ScalePercent/100), so this
	/// just sets the "100%" reference and render resolution — kept large so the default 500% is
	/// clearly readable a thousand units away in the sky.</summary>
	private const float BasePanelHeight = 1000f;

	/// <summary>The scene's ultimate image module. God powers fire ult images through this.</summary>
	public static GodPowersImageModule Instance { get; private set; }

	protected override void OnEnabled()
	{
		if ( Instance.IsValid() && Instance != this )
			Log.Warning( "[UltImage] A second GodPowersImageModule was enabled — the last one wins." );
		Instance = this;
	}

	protected override void OnDisabled()
	{
		if ( Instance == this ) Instance = null;
	}

	protected override void OnStart()
	{
		if ( !DebugSpawnMaatOnStart ) return;

		// Fire it from ONE place only. In a session that's the host (its broadcast reaches
		// everyone, exactly like a real ultimate); in the editor / single-player there's no
		// session, so we just spawn locally. Firing from every client would N²-spawn.
		if ( Networking.IsActive && !Networking.IsHost ) return;

		Invoke( DebugSpawnDelay, ShowMaatImage );
		if ( DebugLog ) Log.Info( $"[UltImage] Debug: Ma'at image scheduled in {DebugSpawnDelay}s." );
	}

	// ───────────── Public API — one per god, called on Ultimate activation ─────────────

	public void ShowTaranisImage() => Show( GodImage.Taranis );
	public void ShowMaatImage() => Show( GodImage.Maat );
	public void ShowDionysosImage() => Show( GodImage.Dionysos );
	public void ShowLavernaImage() => Show( GodImage.Laverna );

	// ───────────── Broadcast → per-client local spawn ─────────────

	/// <summary>Fire the image on every client (each aims it at its own chariot). No handle kept.</summary>
	private void Show( GodImage god )
	{
		if ( Networking.IsActive )
			ShowRpc( (int)god );
		else
			ShowLocal( (int)god );
	}

	[Rpc.Broadcast]
	private void ShowRpc( int god ) => ShowLocal( god );

	/// <summary>Spawn the image for THIS client, hung in the sky in front of this client's chariot.</summary>
	private void ShowLocal( int godInt )
	{
		var god = (GodImage)godInt;
		var tex = Resolve( god );
		if ( tex is null )
		{
			if ( DebugLog ) Log.Warning( $"[UltImage] {god} has no Texture assigned." );
			return;
		}

		var chariot = FindLocalChariot();
		if ( chariot is null )
		{
			if ( DebugLog ) Log.Warning( "[UltImage] No local chariot found — image skipped." );
			return;
		}

		// Position: Distance units along the chariot's facing, Height units straight up.
		var pos = chariot.WorldPosition
			+ chariot.WorldRotation.Forward * Distance
			+ Vector3.Up * Height;

		// Initial facing back toward the chariot; WorldPanel.LookAtCamera then keeps it
		// pointed at the viewer every frame.
		var toChariot = (chariot.WorldPosition - pos).Normal;
		var rot = Rotation.LookAt( toChariot, Vector3.Up );

		var go = Scene.CreateObject();
		go.Name = $"GodPowerSkyImage ({god})";
		go.WorldPosition = pos;
		go.WorldRotation = rot;
		// PHYSICAL size is the transform scale, NOT WorldPanel.RenderScale (that only changes
		// the panel's render RESOLUTION, which is why the % slider looked dead). Driving
		// WorldScale off ScalePercent is what actually resizes the image. 500% => 5×.
		go.WorldScale = ScalePercent / 100f;

		// Panel host: sized (in panel-pixels) to the image's aspect so it isn't stretched,
		// billboarding at the camera so it always faces the viewer.
		var panel = go.Components.Create<WorldPanel>();
		panel.LookAtCamera = true;
		panel.RenderScale = 1f;             // full render resolution (crisp) — does NOT set world size
		panel.RenderOptions.Game = true;    // draw on the in-game world layer

		float aspect = tex.Height > 0 ? (float)tex.Width / tex.Height : 1f;
		panel.PanelSize = new Vector2( BasePanelHeight * aspect, BasePanelHeight );

		// The picture itself. Draws the texture and self-destructs after Duration.
		var image = go.Components.Create<GodPowerSkyImage>();
		image.Picture = tex;
		image.Lifetime = Duration;

		if ( DebugLog ) Log.Info( $"[UltImage] {god} image at {pos}, scale {ScalePercent}% (WorldScale {go.WorldScale.x:0.##}), tex '{tex.ResourcePath}' {tex.Width}x{tex.Height}, {Duration}s." );
	}

	/// <summary>This client's own chariot: the one ChariotPhysics body that isn't a network proxy.</summary>
	private GameObject FindLocalChariot()
	{
		foreach ( var chariot in Scene.GetAllComponents<ChariotPhysics>() )
		{
			if ( !chariot.GameObject.IsProxy )
				return chariot.GameObject;
		}
		return null;
	}

	private Texture Resolve( GodImage god ) => god switch
	{
		GodImage.Taranis => TaranisImage,
		GodImage.Maat => MaatImage,
		GodImage.Dionysos => DionysosImage,
		GodImage.Laverna => LavernaImage,
		_ => null,
	};
}
