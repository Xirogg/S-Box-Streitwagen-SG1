namespace Sandbox;

public sealed class HorseVisuals : Component
{
	[Property, Group( "Horse" )] private GameObject RightHorse;
	[Property, Group( "Horse" )] private GameObject LeftHorse;


	protected override void OnFixedUpdate()
	{
		
	}
}
