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
	}

	[Rpc.Broadcast]
	public void SetReady( bool ready )
	{
		if ( Networking.IsHost )
			IsReady = ready;
	}
}
