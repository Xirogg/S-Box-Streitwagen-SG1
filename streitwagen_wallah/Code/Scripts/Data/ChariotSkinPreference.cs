using Sandbox;
using System;

/// <summary>
/// Die beiden wählbaren Wagen-Skins. Beide stecken im SELBEN Modell
/// (<c>models/chariot/m_cherd_rig_2.vmdl</c>); der Krokodilkopf ist ein separates Submesh
/// ("KrokoKopfSeperat"), das über eine Body-Group ein-/ausgeblendet wird.
///   - Krokodilkopf (Standard): KrokoKopfSeperat sichtbar
///   - Pferdekopf:              KrokoKopfSeperat ausgeblendet
/// </summary>
public enum ChariotSkin
{
	Krokodilkopf,   // 0 – Standard-Look (aktuell)
	Pferdekopf      // 1 – Krokodilkopf-Submesh aus
}

/// <summary>
/// Nur-lokale Wahl des Wagen-Skins (pro Spieler/Rechner). Bewusst KEIN Networking und kein
/// Component: die Wahl trifft der Spieler in der Lobby, gebraucht wird sie erst beim Rennen auf
/// dem eigenen Wagen. Der statische Wert überlebt den Szenenwechsel Lobby -> Rennen (gleicher
/// Prozess); zusätzlich wird er in <see cref="FileSystem.Data"/> gespeichert, damit er auch einen
/// Neustart übersteht (gleiches Muster wie der lokale Nickname in <see cref="PlayerNameManager"/>).
///
/// Die Verteilung an die anderen Spieler übernimmt <see cref="HorseVisuals"/>: der Besitzer des
/// Wagens liest hier seine Wahl und spiegelt sie per [Sync] an alle Peers.
/// </summary>
public static class ChariotSkinPreference
{
	/// <summary> Datei in FileSystem.Data mit dem zuletzt gewählten Skin dieses Rechners. </summary>
	public const string SkinFile = "chariot_skin.txt";

	static ChariotSkin? _cached;

	/// <summary> Aktuell gewählter Skin. Setzen speichert zusätzlich auf Platte. </summary>
	public static ChariotSkin Current
	{
		get
		{
			_cached ??= LoadFromDisk();
			return _cached.Value;
		}
		set
		{
			if ( _cached == value ) return;
			_cached = value;
			SaveToDisk( value );
		}
	}

	/// <summary> Zwischen den beiden Skins wechseln. </summary>
	public static void Toggle()
		=> Current = Current == ChariotSkin.Krokodilkopf ? ChariotSkin.Pferdekopf : ChariotSkin.Krokodilkopf;

	static ChariotSkin LoadFromDisk()
	{
		try
		{
			if ( FileSystem.Data.FileExists( SkinFile )
				&& Enum.TryParse<ChariotSkin>( (FileSystem.Data.ReadAllText( SkinFile ) ?? "").Trim(), out var v ) )
				return v;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[ChariotSkin] Konnte '{SkinFile}' nicht laden: {e.Message}" );
		}
		return ChariotSkin.Krokodilkopf; // Default = aktueller Look
	}

	static void SaveToDisk( ChariotSkin v )
	{
		try
		{
			FileSystem.Data.WriteAllText( SkinFile, v.ToString() );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[ChariotSkin] Konnte '{SkinFile}' nicht speichern: {e.Message}" );
		}
	}
}
