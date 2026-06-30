namespace Sandbox;

/// <summary>
/// Zentrales Sound-Design für die Lobby-UI. Spielt UI-Klick-Sounds rein LOKAL ab.
///
/// Multiplayer-tauglich ohne Networking: <see cref="Sound.Play(SoundEvent)"/> läuft
/// NUR auf dem Client, der die Methode aufruft. Die Razor-Button-Handler
/// (ToggleReady, CycleTrack, Screen-Wechsel) laufen ohnehin lokal beim klickenden
/// Spieler – also hört ausschließlich dieser Spieler den Klick-Sound. Es wird
/// bewusst KEIN RPC/Broadcast verwendet, sonst würde der Sound bei allen knallen.
/// </summary>
public sealed class LobbyUISFX : Component
{
	/// <summary>
	/// Singleton, damit die Razor-Screens (LobbyGUI/AltarGUI) den lokalen
	/// SFX-Spieler erreichen – analog zu <c>LobbyManager.Instance</c>. Pro Client
	/// existiert genau eine Instanz (lokales Szenen-Objekt), daher zeigt Instance
	/// immer auf den eigenen Client.
	/// </summary>
	public static LobbyUISFX Instance { get; private set; }

	/// <summary>
	/// "Sound A" – wird bei jedem der vier Lobby-Buttons abgespielt (Bereit,
	/// Charakter/Altar, Strecke, Altar-Zurück). Im Inspector die .sound-Datei
	/// zuweisen.
	/// </summary>
	[Property] public SoundEvent ButtonSound { get; set; }

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	/// <summary>
	/// Spielt den Button-Klick-Sound (Sound A) lokal ab – 2D (ohne Position), da
	/// reines UI. Wird von den Lobby-/Altar-Button-Handlern aufgerufen. No-op,
	/// solange im Inspector kein Sound zugewiesen ist.
	/// </summary>
	public void PlayButtonSound()
	{
		if ( ButtonSound is null )
			return;

		Sound.Play( ButtonSound );
	}
}
