using Sandbox;
using LapSystem;
using System;
using System.Collections.Generic;

public sealed class HorseController : Component, ISpeedModifiable
{
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

	[Property, Group( "Movement" )] public float PullForce { get; set; } = 6000f;
	[Property, Group( "Movement" )] public float MaxSpeed { get; set; } = 400f;
	[Property, Group( "Movement" )] public float BrakeForce { get; set; } = 2000f;
	[Property, Group( "Movement" )] public float ReverseForce { get; set; } = 1500f;
	[Property, Group( "Movement" )] public float MaxReverseSpeed { get; set; } = 150f;

	[Property, Group( "Steering" )] public float SteerTorque { get; set; } = 160000f;
	[Property, Group( "Steering" )] public float MaxAngularSpeed { get; set; } = 180f;
	[Property, Group( "Steering" )] public bool ClampAngular { get; set; } = false;
	[Property, Group( "Steering" ), Range( 0f, 1f )] public float LowSpeedSteerScale { get; set; } = 0.35f;
	[Property, Group( "Steering" )] public float SteerFullEffectSpeed { get; set; } = 200f;

	[Property, Group( "Stability" ), Range( 0f, 30f )] public float LateralGrip { get; set; } = 3f;

	[Property, Group( "Debug" )] public bool DebugLog { get; set; } = true;

	[RequireComponent] public Rigidbody Body { get; set; }

	private Vector2 _moveInput;
	private float _debugTimer;

	protected override void OnUpdate()
	{
		if ( RaceManager.Instance?.StartCountdownTimeLeft > 0f )
		{
			_moveInput = Vector2.Zero;
			return;
		}

		float vertical = 0f;
		float horizontal = 0f;

		if ( Input.Down( "Forward" ) ) vertical += 1f;
		if ( Input.Down( "Backward" ) ) vertical -= 1f;
		if ( Input.Down( "Left" ) ) horizontal -= 1f;
		if ( Input.Down( "Right" ) ) horizontal += 1f;

		_moveInput = new Vector2( horizontal, vertical );
	}

	protected override void OnFixedUpdate()
	{
		ApplyLocomotion( _moveInput.y );
		ApplySteering( _moveInput.x );
		ApplyLateralGrip();
		ClampAngularSpeed();

		if ( DebugLog )
		{
			_debugTimer += Time.Delta;
			if ( _debugTimer >= 0.5f )
			{
				_debugTimer = 0f;
				float fwdSpeed = Vector3.Dot( Body.Velocity, WorldRotation.Forward );
				Log.Info( $"[Horse] In=({_moveInput.x:F1},{_moveInput.y:F1}) | Fwd={fwdSpeed:F1} | Spd={Body.Velocity.Length:F1} | AngVelZ={Body.AngularVelocity.z:F2}" );
			}
		}
	}

	private void ApplyLocomotion( float throttle )
	{
		Vector3 forward = WorldRotation.Forward;
		float fwdSpeed = Vector3.Dot( Body.Velocity, forward );

		if ( throttle > 0.01f )
		{
			if ( fwdSpeed < MaxSpeed * _speedMultiplier )
			{
				Body.ApplyForce( forward * PullForce * _speedMultiplier * throttle );
			}
		}
		else if ( throttle < -0.01f )
		{
			float strength = -throttle;
			if ( fwdSpeed > 0.5f )
			{
				Body.ApplyForce( -forward * BrakeForce * strength );
			}
			else if ( fwdSpeed > -MaxReverseSpeed )
			{
				Body.ApplyForce( -forward * ReverseForce * strength );
			}
		}
	}

	private void ApplySteering( float steerInput )
	{
		if ( MathF.Abs( steerInput ) < 0.01f )
			return;

		float speed = Body.Velocity.Length;
		float speedFactor = MathF.Min( speed / MathF.Max( SteerFullEffectSpeed, 1f ), 1f );
		float scale = MathX.Lerp( LowSpeedSteerScale, 1f, speedFactor );

		Vector3 torque = Vector3.Up * SteerTorque * steerInput * scale;
		Body.ApplyTorque( torque );
	}

	private void ApplyLateralGrip()
	{
		if ( LateralGrip <= 0f ) return;

		// Project to horizontal so grip doesn't fight gravity on slopes.
		Vector3 right = WorldRotation.Right.WithZ( 0f );
		if ( right.LengthSquared < 0.0001f ) return;
		right = right.Normal;

		float lateralAmount = Vector3.Dot( Body.Velocity, right );
		float killFactor = 1f - MathF.Exp( -LateralGrip * Time.Delta );
		Body.Velocity -= right * (lateralAmount * killFactor);
	}

	private void ClampAngularSpeed()
	{
		if ( !ClampAngular ) return;

		Vector3 angVel = Body.AngularVelocity;
		float yaw = angVel.z;
		if ( MathF.Abs( yaw ) > MaxAngularSpeed )
		{
			yaw = MathF.Sign( yaw ) * MaxAngularSpeed;
			Body.AngularVelocity = new Vector3( angVel.x, angVel.y, yaw );
		}
	}
}
