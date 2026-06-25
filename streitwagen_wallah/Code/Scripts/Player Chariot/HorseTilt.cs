using Sandbox;
using System;

/// <summary>
/// Lets a single horse visually bank (roll) into corners without touching the
/// driving physics. Purely cosmetic: it reads how fast the drive body (the
/// Antrieb <see cref="Rigidbody"/> this horse hangs under) is yawing and how
/// fast it's moving, turns that into a target roll angle, and eases the horse's
/// local rotation toward it — hard-capped at <see cref="MaxLeanAngle"/>.
///
/// Put one on EACH horse so they tilt independently: every instance writes only
/// its own <c>LocalRotation</c>, so the two horses can lean by different amounts
/// (give them different settings if you want). The ram BoxCollider sits on the
/// same GameObject, so it banks along with the model — the collider tilts in
/// curves too.
///
/// Why scripted instead of joints? The two horses share one drive Rigidbody and
/// the driving feel is already tuned. A scripted lean keeps that untouched and
/// lets us guarantee both the lean direction and the cap.
/// </summary>
public sealed class HorseTilt : Component
{
	/// <summary>
	/// The body whose motion drives the lean — normally the Antrieb Rigidbody
	/// this horse is parented under. Auto-found from the parents if left empty.
	/// </summary>
	[Property, Group( "Setup" )] public Rigidbody DriveBody { get; set; }

	/// <summary>Hard cap on the bank angle, in degrees. The horse never rolls past this.</summary>
	[Property, Group( "Lean" ), Range( 0f, 60f )] public float MaxLeanAngle { get; set; } = 18f;

	/// <summary>
	/// Degrees of lean per degree/second of turn rate (before the cap). Higher =
	/// banks harder for the same corner. The result is clamped to
	/// <see cref="MaxLeanAngle"/>, so this mostly shapes how quickly gentle turns
	/// build toward the full lean.
	/// </summary>
	[Property, Group( "Lean" )] public float LeanStrength { get; set; } = 0.35f;

	/// <summary>
	/// Planar speed (units/s) at which the lean reaches full responsiveness.
	/// Below it the lean is scaled down so the horse doesn't bank while turning
	/// in place or crawling — it only leans in real, moving corners.
	/// </summary>
	[Property, Group( "Lean" )] public float FullLeanSpeed { get; set; } = 400f;

	/// <summary>
	/// How fast the current lean eases toward the target (per second). Higher =
	/// snappier, lower = floatier. Exponential, so it's framerate-independent.
	/// </summary>
	[Property, Group( "Lean" )] public float LeanResponse { get; set; } = 8f;

	/// <summary>
	/// Flip this if the horse banks the wrong way (away from the corner instead
	/// of into it). Axis sign conventions are easy to get backwards — toggle it
	/// in the editor rather than editing code.
	/// </summary>
	[Property, Group( "Lean" )] public bool InvertLean { get; set; }

	private Rotation _baseLocalRotation;
	private float _currentLean;
	private float _prevYaw;
	private Vector3 _prevPos;
	private bool _seeded;

	protected override void OnStart()
	{
		// Remember the horse's authored local orientation so the lean is applied
		// on top of it instead of replacing it.
		_baseLocalRotation = LocalRotation;

		DriveBody ??= Components.Get<Rigidbody>( FindMode.EverythingInSelfAndAncestors );

		if ( DriveBody is null )
			Log.Warning( $"[HorseTilt] No drive Rigidbody found for '{GameObject.Name}' — the horse won't lean." );
	}

	protected override void OnUpdate()
	{
		if ( DriveBody is null )
			return;

		float dt = Time.Delta;
		if ( dt <= 0f )
			return;

		float yaw = DriveBody.WorldRotation.Angles().yaw;
		Vector3 pos = DriveBody.WorldPosition;

		// First frame: seed the deltas so we don't get a one-frame spike from
		// comparing against zero.
		if ( !_seeded )
		{
			_prevYaw = yaw;
			_prevPos = pos;
			_seeded = true;
			return;
		}

		// Turn rate (deg/s) around up, and planar speed (units/s). Both are read
		// from the drive body's transform — which is networked — so this produces
		// the same lean on the owner and on every other client without having to
		// sync the horse rotation itself.
		float yawRate = MathX.DeltaDegrees( _prevYaw, yaw ) / dt;
		float planarSpeed = (pos - _prevPos).WithZ( 0f ).Length / dt;
		_prevYaw = yaw;
		_prevPos = pos;

		float speedFactor = MathX.Clamp( planarSpeed / MathF.Max( FullLeanSpeed, 1f ), 0f, 1f );

		// Bank into the corner by default (the roll opposes the turn direction).
		// The clamp is the "max rotation" cap the design calls for.
		float target = yawRate * LeanStrength * speedFactor;
		if ( !InvertLean ) target = -target;
		target = MathX.Clamp( target, -MaxLeanAngle, MaxLeanAngle );

		// Ease toward the target, framerate-independent — same exp-smoothing the
		// chariot/horse grip code uses.
		float k = 1f - MathF.Exp( -LeanResponse * dt );
		_currentLean += (target - _currentLean) * k;

		// Roll is rotation about the forward (X) axis → a sideways bank. Applied
		// in local space so it stacks on the authored rotation and rides along
		// with the drive body's yaw.
		LocalRotation = _baseLocalRotation * Rotation.FromRoll( _currentLean );
	}
}
