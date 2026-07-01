using System;

namespace Sandbox;

/// <summary>
/// Step-Climb Assist for the chariot's physics bodies.
///
/// Problem it solves: the drive/cart rigidbodies rest on the concave racetrack mesh
/// with flat/mesh colliders. Where two triangles meet at a small lip, the leading face
/// bites the seam and forward momentum is dumped into the contact normal — the "stuck on
/// a tiny bump" stall.
///
/// This component probes ahead in the TRAVEL direction each physics tick. If it finds a
/// small rise it can climb (height &lt;= <see cref="MaxStepHeight"/>) it adds just enough
/// UPWARD velocity to ride over it — horizontal velocity is left untouched, so momentum
/// is preserved instead of lost. If the obstruction is taller than <see cref="MaxStepHeight"/>
/// (a wall) it does nothing and lets normal collision stop the body. Same principle as a
/// CharacterController's StepHeight, applied to a Rigidbody.
///
/// Because it discriminates by obstruction HEIGHT, it needs no collision-layer/tag changes
/// and no map edits: small seams get climbed, tall walls block naturally. Put one on the
/// horse drive body (Antrieb); optionally add a second on the cart (Wagen).
///
/// Owner-only (networked rigidbodies are only simulated by their owner) and runs in
/// FixedUpdate, matching TestControlls / ChariotPhysics.
/// </summary>
public sealed class ClimbAssist : Component
{
	/// <summary>
	/// The rigidbody to assist. If left empty, the Rigidbody on this GameObject is used.
	/// </summary>
	[Property, Group( "Setup" )] public Rigidbody Body { get; set; }

	/// <summary>
	/// Vertical offset (cm) from the body origin DOWN to its ground-contact height ("foot"
	/// level). Set this so the probe sits at the bottom of the collider that touches the
	/// track. Turn on <see cref="DebugLog"/> and tune until steps are detected reliably.
	/// </summary>
	[Property, Group( "Setup" )] public float FootOffset { get; set; } = 25f;

	/// <summary>
	/// Maximum rise (cm) the body will climb. Anything taller is treated as a wall and left
	/// to normal collision. THE main knob: raise it to glide over bigger lips, lower it if
	/// the body starts climbing things it shouldn't (e.g. wall bases).
	/// </summary>
	[Property, Group( "Step" ), Range( 1f, 200f )] public float MaxStepHeight { get; set; } = 35f;

	/// <summary>
	/// Ignore rises smaller than this (cm) so the assist doesn't fire on flat-ground noise.
	/// </summary>
	[Property, Group( "Step" ), Range( 0f, 30f )] public float MinStepHeight { get; set; } = 2f;

	/// <summary>
	/// Look-ahead distance (cm) at low speed. The effective look-ahead grows with speed via
	/// <see cref="LookAheadPerSpeed"/> so fast approaches are caught earlier.
	/// </summary>
	[Property, Group( "Probe" )] public float LookAhead { get; set; } = 45f;

	/// <summary>
	/// Extra look-ahead (cm) per unit of horizontal speed. e.g. 0.03 at 2000 u/s adds 60cm.
	/// </summary>
	[Property, Group( "Probe" )] public float LookAheadPerSpeed { get; set; } = 0.03f;

	/// <summary>
	/// Number of forward probes spread across the leading edge (odd numbers give a centre
	/// ray + symmetric pairs). More probes catch corner snags a single centre ray misses.
	/// </summary>
	[Property, Group( "Probe" ), Range( 1, 5 )] public int ProbeCount { get; set; } = 3;

	/// <summary>
	/// Half-width (cm) the outer probes spread to, left/right of centre. Set to roughly half
	/// the body's collider width so the outer rays sit near the leading corners.
	/// </summary>
	[Property, Group( "Probe" )] public float ProbeHalfWidth { get; set; } = 30f;

	/// <summary>
	/// Only assist when ground is within this distance (cm) below the foot, so the body can't
	/// "climb" while airborne (mid-jump over a ramp). Keep &gt;= <see cref="MaxStepHeight"/>.
	/// </summary>
	[Property, Group( "Probe" )] public float MaxGroundDistance { get; set; } = 60f;

	/// <summary>
	/// Minimum horizontal speed (cm/s) before the assist engages. Below this the body isn't
	/// really driving into anything, so physics is left alone.
	/// </summary>
	[Property, Group( "Tuning" )] public float MinMoveSpeed { get; set; } = 30f;

	/// <summary>
	/// Scales the computed climb speed. 1 = just enough to reach the step top by the time the
	/// body arrives; &gt;1 = climb more eagerly (snappier, but bouncier).
	/// </summary>
	[Property, Group( "Tuning" ), Range( 0.5f, 3f )] public float ClimbBoost { get; set; } = 1.25f;

	/// <summary>
	/// Hard cap (cm/s) on the upward velocity the assist may add, so a close/tall step can't
	/// launch the body.
	/// </summary>
	[Property, Group( "Tuning" )] public float MaxClimbSpeed { get; set; } = 400f;

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = false;

	// Height (cm) above the foot at which the forward "is something blocking me?" ray is cast.
	// Small so even low lips are seen, but non-zero so the ray doesn't start inside the floor.
	private const float ForwardRayHeight = 2f;

	// Nudge (cm) past the detected face before dropping the step-top probe, so it lands on the
	// step's top surface rather than clipping the face itself.
	private const float StepTopProbeInset = 4f;

	private float _debugTimer;

	protected override void OnStart()
	{
		Body ??= Components.Get<Rigidbody>();
		if ( Body is null )
			Log.Warning( "[ClimbAssist] No Rigidbody assigned or found on this GameObject — component will do nothing." );
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;
		if ( !Body.IsValid() ) return;

		Vector3 velocity = Body.Velocity;
		Vector3 horizontal = velocity.WithZ( 0f );
		float speed = horizontal.Length;
		if ( speed < MinMoveSpeed ) return;

		Vector3 travelDir = horizontal / speed;                       // horizontal, unit
		Vector3 right = Vector3.Cross( Vector3.Up, travelDir ).Normal; // horizontal, ⟂ to travel

		// Foot = ground-contact reference directly under the body origin.
		Vector3 foot = Body.WorldPosition + Vector3.Down * FootOffset;

		// Don't yank the body up while airborne.
		if ( !IsNearGround( foot ) ) return;

		float effectiveLookAhead = LookAhead + speed * LookAheadPerSpeed;

		float bestRise = 0f;                    // tallest CLIMBABLE rise across the probes
		float bestDistance = effectiveLookAhead;

		int count = Math.Max( ProbeCount, 1 );
		for ( int i = 0; i < count; i++ )
		{
			// Spread -1..+1 across the width (single probe -> centre only).
			float t = count == 1 ? 0f : ((float)i / (count - 1)) * 2f - 1f;
			Vector3 probeFoot = foot + right * (t * ProbeHalfWidth);

			if ( !EvaluateProbe( probeFoot, travelDir, effectiveLookAhead, out float rise, out float distance ) )
				continue;

			// A wall was seen on this probe -> never climb, abort this tick.
			if ( rise < 0f ) return;

			if ( rise > bestRise )
			{
				bestRise = rise;
				bestDistance = distance;
			}
		}

		if ( bestRise < MinStepHeight ) return;

		// Vertical speed needed to have climbed 'bestRise' by the time we reach the step at
		// the current horizontal speed. Only touches Z, so horizontal momentum is preserved.
		float timeToReach = MathF.Max( bestDistance / speed, Time.Delta );
		float requiredUp = MathX.Clamp( (bestRise / timeToReach) * ClimbBoost, 0f, MaxClimbSpeed );

		// Only ever ADD upward speed — never cancel an existing faster climb/fall.
		if ( requiredUp > velocity.z )
			Body.Velocity = velocity.WithZ( requiredUp );

		if ( DebugLog )
		{
			_debugTimer += Time.Delta;
			if ( _debugTimer >= 0.25f )
			{
				_debugTimer = 0f;
				Log.Info( $"[ClimbAssist] step rise={bestRise:F1}cm dist={bestDistance:F0} -> up={requiredUp:F0} (spd={speed:F0})" );
			}
		}
	}

	/// <summary>
	/// Probes one line ahead. Returns true if an obstruction was found. On return
	/// <paramref name="rise"/> is either a positive climbable step height (cm), or -1 when the
	/// obstruction is a WALL (taller than <see cref="MaxStepHeight"/> or a steep face) — in
	/// which case the caller must abort so we never climb a wall.
	/// </summary>
	private bool EvaluateProbe( Vector3 probeFoot, Vector3 dir, float lookAhead, out float rise, out float distance )
	{
		rise = 0f;
		distance = lookAhead;

		// 1) Forward ray just above foot level: is anything blocking the path ahead?
		Vector3 from = probeFoot + Vector3.Up * ForwardRayHeight;
		Vector3 to = from + dir * lookAhead;
		var forward = Scene.Trace.Ray( from, to )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		if ( !forward.Hit ) return false;      // clear ahead (flat / downhill) — nothing to do
		if ( forward.Distance < 1f ) return false; // started against/inside a surface — ignore

		distance = forward.Distance;

		// 2) Is the obstruction low enough to be a step? Drop a ray from MaxStepHeight above the
		//    hit (nudged just past the face) and look for a walkable top surface.
		Vector3 hitPoint = from + dir * forward.Distance;
		Vector3 probeTop = hitPoint + dir * StepTopProbeInset + Vector3.Up * MaxStepHeight;
		float dropLength = MaxStepHeight * 2f + 10f;
		var down = Scene.Trace.Ray( probeTop, probeTop + Vector3.Down * dropLength )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		if ( !down.Hit )
		{
			rise = -1f;                        // no top within reach -> obstruction is a wall
			return true;
		}

		float topZ = probeTop.z - down.Distance;
		float stepRise = topZ - probeFoot.z;

		if ( stepRise > MaxStepHeight )
		{
			rise = -1f;                        // top higher than we can climb -> wall
			return true;
		}

		// Guard: the "top" must be roughly flat (normal mostly up), otherwise we clipped a
		// steep face rather than a genuine step top.
		if ( Vector3.Dot( down.Normal, Vector3.Up ) < 0.5f )
		{
			rise = -1f;
			return true;
		}

		rise = stepRise;                       // caller filters with MinStepHeight
		return true;
	}

	/// <summary>Down-ray from the foot to confirm the body is on/near the ground.</summary>
	private bool IsNearGround( Vector3 foot )
	{
		Vector3 from = foot + Vector3.Up * 5f;
		var tr = Scene.Trace.Ray( from, from + Vector3.Down * (MaxGroundDistance + 5f) )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();
		return tr.Hit;
	}
}
