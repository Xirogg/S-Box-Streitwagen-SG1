using System;
using System.Collections.Generic;
using Sandbox;

public sealed class LightningStrikeLoop : Component
{
	[Property, Category( "Setup" )] public Material BoltMaterial { get; set; }
	[Property, Category( "Setup" )] public Vector3 StartOffset { get; set; } = new Vector3( 0, 0, 500 );
	[Property, Category( "Setup" )] public Vector3 EndOffset { get; set; } = new Vector3( 0, 0, 0 );

	[Property, Category( "Loop" )] public bool AutoLoop { get; set; } = true;
	[Property, Category( "Loop" )] public float MinInterval { get; set; } = 1.0f;
	[Property, Category( "Loop" )] public float MaxInterval { get; set; } = 3.0f;

	[Property, Category( "Look" )] public float Width { get; set; } = 12f;
	[Property, Category( "Look" )] public Color BoltColor { get; set; } = Color.White;

	[Property, Category( "Timing" )] public float GrowTime { get; set; } = 0.05f;
	[Property, Category( "Timing" )] public float HoldTime { get; set; } = 0.08f;
	[Property, Category( "Timing" )] public float FadeTime { get; set; } = 0.25f;

	[Property, Category( "Form" )] public int Segments { get; set; } = 12;
	[Property, Category( "Form" )] public float Jitter { get; set; } = 30f;
	[Property, Category( "Form" )] public int BranchCount { get; set; } = 4;

	private TimeSince _timeSinceLastStrike;
	private float _nextInterval;

	protected override void OnStart()
	{
		Log.Info( "[LightningStrikeLoop] OnStart läuft." );
		if ( BoltMaterial == null )
			Log.Warning( "[LightningStrikeLoop] KEIN Material zugewiesen!" );

		_nextInterval = Game.Random.Float( MinInterval, MaxInterval );
		_timeSinceLastStrike = 0;

		SpawnStrike();
	}

	protected override void OnUpdate()
	{
		if ( !AutoLoop ) return;

		if ( _timeSinceLastStrike >= _nextInterval )
		{
			SpawnStrike();
			_nextInterval = Game.Random.Float( MinInterval, MaxInterval );
			_timeSinceLastStrike = 0;
		}
	}

	private void SpawnStrike()
	{
		Vector3 start = WorldPosition + StartOffset;
		Vector3 end = WorldPosition + EndOffset;

		var strikeGo = new GameObject( true, "lightning_strike" ) { Parent = GameObject };
		var runner = strikeGo.Components.Create<LightningStrikeInstance>();
		runner.Setup( this, start, end );
	}
}

public sealed class LightningStrikeInstance : Component
{
	private LightningStrikeLoop _owner;
	private readonly List<GameObject> _parts = new();

	private enum State { Growing, Holding, FadingOut, Done }
	private State _state = State.Growing;
	private float _timer;

	public void Setup( LightningStrikeLoop owner, Vector3 start, Vector3 end )
	{
		_owner = owner;

		Vector3 camPos = Scene?.Camera?.WorldPosition ?? (start + Vector3.Forward * 1000f);

		var mainPath = BuildJaggedPath( start, end, owner.Segments, owner.Jitter );
		SpawnSegments( mainPath, owner.Width, owner.BoltColor, camPos );

		for ( int i = 0; i < owner.BranchCount; i++ )
		{
			int startIndex = Game.Random.Int( 1, Math.Max( 1, mainPath.Count - 2 ) );
			Vector3 branchStart = mainPath[startIndex];
			Vector3 dirDown = (end - start).Normal;
			Vector3 randomOffset = new Vector3( Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ), 0 ) * owner.Jitter * 3f;
			Vector3 branchEnd = branchStart + dirDown * Game.Random.Float( 40, 120 ) + randomOffset;

			var branchPath = BuildJaggedPath( branchStart, branchEnd, Game.Random.Int( 3, 5 ), owner.Jitter * 0.6f );
			SpawnSegments( branchPath, owner.Width * 0.4f, owner.BoltColor.WithAlpha( 0.6f ), camPos );
		}

		Log.Info( $"[LightningStrikeInstance] {_parts.Count} Segmente erzeugt." );

		_timer = 0f;
		_state = State.Growing;
	}

	private List<Vector3> BuildJaggedPath( Vector3 from, Vector3 to, int segments, float jitter )
	{
		var points = new List<Vector3> { from };
		for ( int i = 1; i < segments; i++ )
		{
			float t = i / (float)segments;
			Vector3 basePoint = Vector3.Lerp( from, to, t );
			float falloff = MathF.Sin( t * MathF.PI );
			Vector3 offset = new Vector3( Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ), 0 ) * jitter * falloff;
			points.Add( basePoint + offset );
		}
		points.Add( to );
		return points;
	}

	private void SpawnSegments( List<Vector3> path, float width, Color color, Vector3 camPos )
	{
		for ( int i = 0; i < path.Count - 1; i++ )
			_parts.Add( CreateBillboardSegment( path[i], path[i + 1], width, color, camPos ) );
	}

	// Baut das Quad direkt aus Weltraum-Eckpunkten, Kamera-ausgerichtet.
	// Keine Rotation.LookAt-Verrechnung mehr -> kein Gimbal-/Degenerationsproblem.
	private GameObject CreateBillboardSegment( Vector3 a, Vector3 b, float width, Color color, Vector3 camPos )
	{
		var go = new GameObject( true, "bolt_segment" ) { Parent = GameObject };
		go.WorldPosition = Vector3.Zero;
		go.WorldRotation = Rotation.Identity;

		Vector3 dir = (b - a).Normal;
		Vector3 mid = (a + b) * 0.5f;
		Vector3 toCam = (camPos - mid).Normal;

		Vector3 side = Vector3.Cross( dir, toCam );
		if ( side.Length < 0.001f ) side = Vector3.Right; // Fallback falls Blickrichtung == Segmentrichtung
		side = side.Normal * (width * 0.5f);

		var v0 = a - side;
		var v1 = a + side;
		var v2 = b + side;
		var v3 = b - side;

		var vertices = new List<SimpleVertex>
		{
			new SimpleVertex( v0, Vector3.Up, Vector3.Forward, new Vector2(0,0) ),
			new SimpleVertex( v1, Vector3.Up, Vector3.Forward, new Vector2(1,0) ),
			new SimpleVertex( v2, Vector3.Up, Vector3.Forward, new Vector2(1,1) ),
			new SimpleVertex( v3, Vector3.Up, Vector3.Forward, new Vector2(0,1) ),
		};
		var indices = new int[] { 0, 1, 2, 0, 2, 3 };

		var mesh = new Mesh( _owner.BoltMaterial ?? Material.Load( "materials/dev/reflectivity_30.vmat" ) );
		mesh.CreateVertexBuffer( vertices.Count, SimpleVertex.Layout, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );

		var mb = new ModelBuilder();
		mb.AddMesh( mesh );
		var model = mb.Create();

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = model;
		renderer.MaterialOverride = _owner.BoltMaterial;
		renderer.Tint = color;

		return go;
	}

	protected override void OnUpdate()
	{
		if ( _state == State.Done ) return;
		_timer += Time.Delta;

		switch ( _state )
		{
			case State.Growing:
				{
					float t = _owner.GrowTime <= 0 ? 1f : _timer / _owner.GrowTime;
					int visibleCount = Math.Clamp( (int)(t * _parts.Count), 0, _parts.Count );
					for ( int i = 0; i < _parts.Count; i++ )
						_parts[i].Enabled = i <= visibleCount;

					if ( t >= 1f ) { _timer = 0f; _state = State.Holding; }
					break;
				}
			case State.Holding:
				{
					SetAlpha( 1f - Game.Random.Float( 0f, 0.3f ) );
					if ( _timer >= _owner.HoldTime ) { _timer = 0f; _state = State.FadingOut; }
					break;
				}
			case State.FadingOut:
				{
					float t = _owner.FadeTime <= 0 ? 1f : _timer / _owner.FadeTime;
					SetAlpha( 1f - t );
					if ( t >= 1f )
					{
						_state = State.Done;
						GameObject.Destroy();
					}
					break;
				}
		}
	}

	private void SetAlpha( float alpha )
	{
		foreach ( var part in _parts )
		{
			var renderer = part.Components.Get<ModelRenderer>();
			if ( renderer == null ) continue;
			var c = renderer.Tint;
			renderer.Tint = new Color( c.r, c.g, c.b, alpha );
		}
	}
}
