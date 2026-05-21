using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>
/// Passiver Streitwagen-Körper. Wird vom HorsePair via HingeJoint (Yaw) gezogen.
/// Der Joint sitzt auf einem auto-erzeugten Pivot-Child, dessen Position automatisch
/// aus der relativen Lage von Pferd und Wagen im Prefab berechnet wird —
/// dadurch können sich die beiden Bodies nicht ineinanderziehen.
/// </summary>
public sealed class ChariotPhysics : Component, Component.ICollisionListener, ISpeedModifiable
{
	[Property, Group( "Joint" )] public Rigidbody HorsePairRb { get; set; }
	[Property, Group( "Joint" )] public GameObject HitchPoint { get; set; }
	[Property, Group( "Joint" )] public float YawLimit { get; set; } = 170f;

	[Property, Group( "Movement" )] public float MaxSpeed { get; set; } = 2500f;

	private readonly Dictionary<string, float> _speedModifiers = new();
	private float _speedMultiplier = 1f;

	public void SetSpeedMultiplier( string key, float multiplier )
	{
		_speedModifiers[key] = multiplier;
		RecomputeSpeedMultiplier();
	}

	public void ClearSpeedMultiplier( string key )
	{
		if ( _speedModifiers.Remove( key ) )
			RecomputeSpeedMultiplier();
	}

	private void RecomputeSpeedMultiplier()
	{
		float m = 1f;
		foreach ( var v in _speedModifiers.Values )
			m *= v;
		_speedMultiplier = m;
	}

	public float EffectiveMaxSpeed => MaxSpeed * _speedMultiplier;

	[Property, Group( "Stability" ), Range( 0f, 30f )] public float LateralGrip { get; set; } = 0f;

	[Property, Group( "Drift" )] public float DriftForce { get; set; } = 24000f;
	[Property, Group( "Drift" )] public float DriftRearOffset { get; set; } = 100f;
	[Property, Group( "Drift" )] public float DriftMinSpeed { get; set; } = 15f;
	[Property, Group( "Drift" )] public float DriftFullSpeed { get; set; } = 120f;
	[Property, Group( "Drift" )] public float DriftMaxYawRate { get; set; } = 10f;
	[Property, Group( "Drift" ), Range( 0f, 5f )] public float ChariotAngularDamping { get; set; } = 0.04f;

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = true;
	[Property, Group( "Debug" ), ReadOnly] public float CurrentSpeed { get; private set; }
	[Property, Group( "Debug" ), ReadOnly] public float DriftAngle { get; private set; }
	[Property] public ParticleEmitter DustEmitter_L { get; set; }
	[Property] public ParticleEmitter DustEmitter_R { get; set; }

	[RequireComponent] public Rigidbody Body { get; set; }

	[Sync] private bool DustActive { get; set; }

	private HingeJoint _joint;
	private GameObject _jointPivot;
	private float _debugTimer;


	private PlayerCollisions _ownerCollisions; 

	protected override void OnStart()
	{
		Tags.Add( "chariot" );
		if ( HorsePairRb is not null )
			HorsePairRb.Tags.Add( "chariot" );

		if ( IsProxy ) return; 

		if ( HorsePairRb is not null )
		{
			SetupJoint( HorsePairRb );
		}
		else
		{
			Log.Warning( "[ChariotPhysics] HorsePairRb ist nicht zugewiesen — Joint wird nicht erstellt." );
		}
	}

	public event Action<Collision> ImpactStarted;

	void Component.ICollisionListener.OnCollisionStart( Collision other )
	{
		if (IsProxy) return;
		if ( _ownerCollisions is null && HorsePairRb is not null )
			_ownerCollisions = HorsePairRb.Components.Get<PlayerCollisions>();
		_ownerCollisions?.HandleChariotCollision( other );
		ImpactStarted?.Invoke( other );
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
		_joint.EnableCollision = true;

		//Log.Info( $"[ChariotPhysics] Joint erstellt — YawLimit=±{YawLimit}" );
		//Log.Info( $"[ChariotPhysics] HorsePos={horseRb.WorldPosition} | ChariotPos={WorldPosition} | PivotWorld={_jointPivot.WorldPosition} | HitchPointSet={HitchPoint is not null}" );
	}

	protected override void OnFixedUpdate()
	{
		// Effekte am Rad — auf allen Clients aus dem synchronisierten Flag gelesen
		if ( DustEmitter_L is not null )
			DustEmitter_L.Enabled = DustActive;

		if ( DustEmitter_R is not null )
			DustEmitter_R.Enabled = DustActive;

		if ( IsProxy ) return;

		UpdateTelemetry();
		DustActive = Body.Velocity.Length > 400f;

		ApplyDriftImpulse();
		ApplyLateralGrip();
		DampenYaw();
		ClampMaxSpeed();


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

	private void ClampMaxSpeed()
	{
		float cap = EffectiveMaxSpeed;
		if ( cap <= 0f ) return;

		Vector3 vel = Body.Velocity;
		float speed = vel.Length;
		if ( speed > cap )
			Body.Velocity = vel * (cap / speed);
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
