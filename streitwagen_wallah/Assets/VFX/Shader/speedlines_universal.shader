HEADER
{
	Description = "Speed Lines (Standalone)";
	DevShader = true;
	Version = 1;
}

MODES
{
	VrForward();
}

FEATURES
{
	#include "common/features.hlsl"
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION  < Semantic( PosXyz ); >;
	float2 vTexCoord   : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float2 vTexCoord : TEXCOORD0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif

	#if ( ( PROGRAM == VFX_PROGRAM_PS ) )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionPs = float4( i.vPositionOs.xy, 0.0f, 1.0f );
		o.vTexCoord = i.vTexCoord;
		return o;
	}
}

PS
{
	#include "postprocess/common.hlsl"

	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, false );

	Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

	// Master amount: how strong / how deep the speed lines reach. Just set it here in the
	// material for a constant effect, or drive it from code with
	// Material.Set( "g_flStrength", value ) if you want it speed-reactive.
	float  g_flStrength  < UiType( Slider ); Range( 0.0, 1.0 );    Default( 0.5 );   UiGroup( "Speedlines,1/0" ); >;

	// Tunable in the material editor
	float  g_flCount     < UiType( Slider ); Range( 16.0, 256.0 ); Default( 96.0 );  UiGroup( "Speedlines,1/1" ); >;
	float  g_flDensity   < UiType( Slider ); Range( 0.0, 1.0 );    Default( 0.72 );  UiGroup( "Speedlines,1/2" ); >;
	float  g_flFalloff   < UiType( Slider ); Range( 0.05, 1.0 );   Default( 0.40 );  UiGroup( "Speedlines,1/3" ); >;
	float  g_flMotion    < UiType( Slider ); Range( 0.0, 3.0 );    Default( 1.0 );   UiGroup( "Speedlines,1/4" ); >;
	float  g_flIntensity < UiType( Slider ); Range( 0.0, 3.0 );    Default( 1.35 );  UiGroup( "Speedlines,1/5" ); >;
	float3 g_vTint       < UiType( Color );  Default3( 1.0, 1.0, 1.0 );              UiGroup( "Speedlines,1/6" ); >;

	// How far the spokes reach toward the centre (0 = edge only, 1 = all the way in).
	float  g_flReachSlow < UiType( Slider ); Range( 0.0, 1.0 ); Default( 0.05 ); UiGroup( "Speedlines Reach,1/1" ); >;
	float  g_flReachFast < UiType( Slider ); Range( 0.0, 1.0 ); Default( 0.55 ); UiGroup( "Speedlines Reach,1/2" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTexCoord;
		float3 scene = g_tColorBuffer.SampleLevel( g_sTrilinearClamp, uv, 0 ).rgb;

		float strength = saturate( g_flStrength );
		if ( strength <= 0.001 )
			return float4( scene, 1.0 );

		float t = g_flTime;

		// Aspect-corrected direction from screen center so the spokes stay radial.
		float texW = 1.0;
		float texH = 1.0;
		g_tColorBuffer.GetDimensions( texW, texH );
		float aspect = texW / max( texH, 1.0 );

		float2 c = uv - 0.5;
		c.x *= aspect;
		float r = length( c ) * 2.0;
		float a = atan2( c.y, c.x );

		float count = max( g_flCount, 1.0 );
		float la = ( a * 0.15915494 + 0.5 ) * count;  // a / (2*PI) + 0.5, scaled to spoke count
		float cell = floor( la );
		float cellW = fmod( cell + count, count );      // wrap the +-PI seam

		// Per-spoke pseudo-random values (inlined hash).
		float h1 = frac( cellW * 0.1031 );
		h1 = frac( h1 * ( h1 + 33.33 ) * ( h1 + h1 + 1.0 ) );
		float h2 = frac( ( cellW + 21.7 ) * 0.1731 );
		h2 = frac( h2 * ( h2 + 19.19 ) * ( h2 + h2 + 1.0 ) );

		// Anti-aliased line at each spoke centre. Toward the middle the spokes converge into
		// sub-pixel slivers, so soften them by their on-screen footprint (fwidth) instead of
		// letting them alias/strobe. random half-thickness per spoke.
		float halfWidth = lerp( 0.06, 0.34, h1 );
		float d = abs( frac( la ) - 0.5 ) * 2.0;          // 0 at spoke core, 1 at cell edge
		float aa = fwidth( d ) + 0.0015;                   // pixel footprint for anti-aliasing
		float core = 1.0 - smoothstep( halfWidth, halfWidth + aa, d );

		// Only a random subset of spokes exist.
		float present = step( 1.0 - g_flDensity, h2 );

		// Reach: edge-only when slow, deeper toward the centre at speed. Driven by the two
		// Reach sliders (higher = deeper). Anti-aliasing above keeps the centre clean.
		float baseInner = lerp( 1.0 - g_flReachSlow, 1.0 - g_flReachFast, strength );
		float reachPulse = 0.06 * sin( t * lerp( 5.0, 11.0, strength ) * g_flMotion + cellW * 4.0 );
		float inner = baseInner + reachPulse * lerp( 0.3, 1.0, h2 );
		float radial = smoothstep( inner, inner + g_flFalloff, r );

		// Per-spoke brightness flicker -- continuous along the line, so no dashing.
		float flick = 0.66 + 0.34 * sin( t * lerp( 8.0, 18.0, strength ) * g_flMotion + cellW * 7.0 + h1 * 6.2831853 );

		float lineMask = core * present * radial * saturate( flick );
		lineMask *= lerp( 0.55, 1.0, h1 );
		lineMask = saturate( lineMask * strength * g_flIntensity );

		float3 outc = lerp( scene, g_vTint, lineMask );
		return float4( outc, 1.0 );
	}
}
