using Sandbox;
using System;

/// <summary>
/// Passiver Streitwagen-Körper. Wird vom HorsePair via HingeJoint (Yaw) gezogen.
/// Der Joint sitzt auf einem auto-erzeugten Pivot-Child, dessen Position automatisch
/// aus der relativen Lage von Pferd und Wagen im Prefab berechnet wird —
/// dadurch können sich die beiden Bodies nicht ineinanderziehen.
/// </summary>
public sealed class ChariotPhysics : Component
{
	[Property, Group( "Joint" )] public Rigidbody HorsePairRb { get; set; }
	[Property, Group( "Joint" )] public GameObject HitchPoint { get; set; }
	[Property, Group( "Joint" )] public float YawLimit { get; set; } = 35f;

	[Property, Group( "Stability" ), Range( 0f, 30f )] public float LateralGrip { get; set; } = 0f;

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = true;
	[Property, Group( "Debug" ), ReadOnly] public float CurrentSpeed { get; private set; }
	[Property, Group( "Debug" ), ReadOnly] public float DriftAngle { get; private set; }

	[RequireComponent] public Rigidbody Body { get; set; }

	private HingeJoint _joint;
	private GameObject _jointPivot;
	private float _debugTimer;

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
		ApplyLateralGrip();
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
