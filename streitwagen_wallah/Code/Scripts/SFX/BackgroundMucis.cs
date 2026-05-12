namespace Sandbox;

public sealed class BackgroundMucis : Component
{
	private static MusicPlayer _player;
	private static string _currentTrackPath;

	[Property] public SoundEvent Music { get; set; }
	[Property, Range( 0f, 15000f )] public float Volume { get; set; } = 11105.8f;

	protected override void OnStart()
	{
		if ( Music is null ) return;

		var path = Music.ResourcePath;
		if ( string.IsNullOrWhiteSpace( path ) ) return;

		if ( _player is not null && _currentTrackPath == path )
		{
			_player.Volume = Volume;
			return;
		}

		StopStaticPlayer();

		_player = MusicPlayer.Play( FileSystem.Mounted, path );
		_player.Volume = Volume;
		_player.Repeat = true;
		_currentTrackPath = path;
	}

	protected override void OnUpdate()
	{
		if ( _player is not null )
			_player.Volume = Volume;
	}

	public static void StopMusic()
	{
		StopStaticPlayer();
	}

	private static void StopStaticPlayer()
	{
		if ( _player is not null )
		{
			_player.Stop();
			_player = null;
			_currentTrackPath = null;
		}
	}
}
