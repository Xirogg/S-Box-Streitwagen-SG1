using Sandbox;
using System;

/// <summary>
/// Rad-Komponente: hängt per HingeJoint an der Plattform (Achse = lokale Y),
/// dämpft seitliches Rutschen (lateral grip) entlang der Achsen-Richtung.
/// Auto-erzeugt den HingeJoint wenn keiner vorhanden ist.
/// </summary>
public sealed class ChariotWheel : Component
{
	[Property, Group( "Joint" )] public Rigidbody Platform { get; set; }
	[Property, Group( "Joint" )] public bool AutoCreateHinge { get; set; } = true;

	[Property, Group( "Grip" ), Range( 0f, 50f )] public float LateralGrip { get; set; } = 12f;
	[Property, Group( "Grip" ), Range( 0f, 5f )] public float RollingDrag { get; set; } = 0.05f;

	[RequireComponent] public Rigidbody Body { get; set; }

	private HingeJoint _hinge;

	protected override void OnStart()
	{
		Tags.Add( "chariot" );

		_hinge = Components.Get<HingeJoint>();

		if ( _hinge is null && AutoCreateHinge && Platform is not null )
		{
			_hinge = Components.Create<HingeJoint>();
			_hinge.Body = Platform.GameObject;
			_hinge.EnableCollision = false;
		}

		if ( _hinge is not null )
			_hinge.EnableCollision = false;
	}

	protected override void OnFixedUpdate()
	{
		if ( LateralGrip > 0f )
		{
			Vector3 axle = WorldRotation.Up;
			float lateralAmount = Vector3.Dot( Body.Velocity, axle );
			float killFactor = 1f - MathF.Exp( -LateralGrip * Time.Delta );
			Body.Velocity -= axle * (lateralAmount * killFactor);
		}

		if ( RollingDrag > 0f )
		{
			Vector3 angVel = Body.AngularVelocity;
			Body.AngularVelocity = angVel * MathF.Exp( -RollingDrag * Time.Delta );
		}
	}
}
