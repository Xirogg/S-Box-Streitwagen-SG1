using Sandbox;
using System;

/// <summary>
/// Passiver Streitwagen-Körper. Wird vom HorsePair via HingeJoint (Yaw) gezogen.
/// Der Joint sitzt auf einem auto-erzeugten Pivot-Child, dessen Position automatisch
/// aus der relativen Lage von Pferd und Wagen im Prefab berechnet wird —
/// dadurch können sich die beiden Bodies nicht ineinanderziehen.
/// </summary>
public sealed class ChariotPhysics : Component, Component.ICollisionListener
{
	[Property, Group( "Joint" )] public Rigidbody HorsePairRb { get; set; }
	[Property, Group( "Joint" )] public GameObject HitchPoint { get; set; }
	[Property, Group( "Joint" )] public float YawLimit { get; set; } = 100f;

	[Property, Group( "Stability" ), Range( 0f, 30f )] public float LateralGrip { get; set; } = 0f;

	[Property, Group( "Drift" )] public float DriftForce { get; set; } = 14000f;
	[Property, Group( "Drift" )] public float DriftRearOffset { get; set; } = 100f;
	[Property, Group( "Drift" )] public float DriftMinSpeed { get; set; } = 20f;
	[Property, Group( "Drift" )] public float DriftFullSpeed { get; set; } = 120f;
	[Property, Group( "Drift" )] public float DriftMaxYawRate { get; set; } = 6f;
	[Property, Group( "Drift" ), Range( 0f, 5f )] public float ChariotAngularDamping { get; set; } = 0.15f;

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = true;
	[Property, Group( "Debug" ), ReadOnly] public float CurrentSpeed { get; private set; }
	[Property, Group( "Debug" ), ReadOnly] public float DriftAngle { get; private set; }

	[RequireComponent] public Rigidbody Body { get; set; }

	private HingeJoint _joint;
	private GameObject _jointPivot;
	private float _debugTimer;


	private PlayerCollisions _ownerCollisions; 

	protected override void OnStart()
	{
		Tags.Add( "chariot" );
		if ( HorsePairRb is not null )
			HorsePairRb.Tags.Add( "chariot" );

		if ( HorsePairRb is not null )
		{
			SetupJoint( HorsePairRb );
		}
		else
		{
			Log.Warning( "[ChariotPhysics] HorsePairRb ist nicht zugewiesen — Joint wird nicht erstellt." );
		}
	}

	void Component.ICollisionListener.OnCollisionStart( Collision other )
	{
		if ( _ownerCollisions is null && HorsePairRb is not null )
			_ownerCollisions = HorsePairRb.Components.Get<PlayerCollisions>();
		_ownerCollisions?.HandleChariotCollision( other );
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision other ) { }
	void Component.ICollisionListener.OnCollisionStop( CollisionStop other ) { }

	public void SetupJoint( Rigidbody horseRb )
	{
		HorsePairRb = horseRb;

		// Pivot sitzt am Deichsel-Joch (HitchPoint), wenn gesetzt — sonst Fallback auf Pferd-Position.
		// Anker am physischen Verbindungspunkt vermeidet Kippmomente beim Ziehen/Lenken.
		Vector3 pivotPos = HitchPoint is not null ? HitchPoint.WorldPosition : horseRb.WorldPosition;

		_jointPivot = new GameObject( true, "ChariotJointPivot" );
		_jointPivot.SetParent( GameObject );
		_jointPivot.WorldPosition = pivotPos;
		_jointPivot.WorldRotation = WorldRotation; // Hinge-Achse = lokales Z = Welt-Up bei Identity

		_joint = _jointPivot.Components.Create<HingeJoint>();
		_joint.Body = horseRb.GameObject;
		_joint.MinAngle = -YawLimit;
		_joint.MaxAngle = YawLimit;
		_joint.EnableCollision = false;

		Log.Info( $"[ChariotPhysics] Joint erstellt — YawLimit=±{YawLimit}" );
		Log.Info( $"[ChariotPhysics] HorsePos={horseRb.WorldPosition} | ChariotPos={WorldPosition} | PivotWorld={_jointPivot.WorldPosition} | HitchPointSet={HitchPoint is not null}" );
	}

	protected override void OnFixedUpdate()
	{
		ApplyDriftImpulse();
		ApplyLateralGrip();
		DampenYaw();
		UpdateTelemetry();

		if ( DebugLog )
		{
			_debugTimer += Time.Delta;
			if ( _debugTimer >= 0.5f )
			{
				_debugTimer = 0f;
				Log.Info( $"[Chariot] Spd={CurrentSpeed:F1} | Drift={DriftAngle:F1}° | Vel={Body.Velocity}" );
			}
		}
	}

	private void ApplyDriftImpulse()
	{
		if ( HorsePairRb is null || DriftForce <= 0f ) return;

		float speed = Body.Velocity.Length;
		if ( speed < DriftMinSpeed ) return;

		float yawRate = MathX.Clamp( HorsePairRb.AngularVelocity.z, -DriftMaxYawRate, DriftMaxYawRate );
		if ( MathF.Abs( yawRate ) < 0.05f ) return;

		float speedFactor = MathX.Clamp( (speed - DriftMinSpeed) / MathF.Max( DriftFullSpeed - DriftMinSpeed, 1f ), 0f, 1f );

		// Push the rear sideways opposite to the turn direction → back end kicks out.
		Vector3 right = WorldRotation.Right;
		Vector3 force = right * (-yawRate * DriftForce * speedFactor);
		Vector3 rearWorld = WorldPosition - WorldRotation.Forward * DriftRearOffset;

		Body.ApplyForceAt( rearWorld, force );
	}

	private void DampenYaw()
	{
		if ( ChariotAngularDamping <= 0f ) return;

		// Light damping only on the chariot's yaw axis — keeps it from spinning out forever
		// without snapping it back behind the horses (that's what makes it feel "loose").
		Vector3 av = Body.AngularVelocity;
		float keep = MathF.Exp( -ChariotAngularDamping * Time.Delta );
		Body.AngularVelocity = new Vector3( av.x, av.y, av.z * keep );
	}

	private void ApplyLateralGrip()
	{
		if ( LateralGrip <= 0f ) return;

		Vector3 right = WorldRotation.Right;
		float lateralAmount = Vector3.Dot( Body.Velocity, right );
		float killFactor = 1f - MathF.Exp( -LateralGrip * Time.Delta );
		Body.Velocity -= right * (lateralAmount * killFactor);
	}

	private void UpdateTelemetry()
	{
		CurrentSpeed = Body.Velocity.Length;
		DriftAngle = CurrentSpeed > 5f
			? Vector3.GetAngle( Body.Velocity.WithZ( 0f ), WorldRotation.Forward.WithZ( 0f ) )
			: 0f;
	}
}
