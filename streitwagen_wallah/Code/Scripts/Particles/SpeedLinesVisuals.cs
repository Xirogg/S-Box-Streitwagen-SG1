namespace Sandbox;

public sealed class SpeedLinesVisuals : Component
{
	[Property] private TestControlls _owner;
	private ParticleSpriteRenderer _renderer;

	protected override void OnAwake()
	{
		_renderer = Components.Get<ParticleSpriteRenderer>();
	}

	protected override void OnUpdate()
	{
		
		if ( _owner == null || !_owner.IsValid() )
		{
			var current = GameObject.Parent;
			while ( current.IsValid() )
			{
				_owner = current.Components.Get<TestControlls>();
				 
				if ( _owner != null ) break;
				current = current.Parent;
			}

			if ( _owner == null ) return;
		}

		bool shouldShow = !_owner.IsProxy;

		if ( _renderer != null && _renderer.Enabled != shouldShow )
			_renderer.Enabled = shouldShow;
	}
}
