using Sandbox;
using System;
using System.Collections.Generic;
public sealed class LobbyPlayer : Component
{
	[Sync] public bool IsReady { get; set; }
	[Sync] public string DisplayName { get; set; }

	protected override void OnStart()
	{
		Log.Info( "Lobby dude is here" ); 
	}
	protected override void OnUpdate()
	{
		if ( !Network.IsOwner )
		{
			return;
		}

		
		if ( Input.Pressed( "Ready" ) )
		{
			Log.Info( "Ready UP" );
			SetReady( !IsReady );
		}

		if ( Input.Pressed( "Change Track" ) )
		{
			RequestCycleTrack();
		}
	}

	[Rpc.Owner]
	public void SetReady( bool ready )
	{
		IsReady = ready;
	}

	[Rpc.Broadcast]
	public void RequestCycleTrack()
	{
		if ( Networking.IsHost )
			LobbyManager.Instance?.CycleTrack();
	}
}
