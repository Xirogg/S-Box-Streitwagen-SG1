using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Nur-lokaler Besitz-Stand der Wagen-Skins (pro Spieler/Rechner). Wie
/// <see cref="ChariotSkinPreference"/> bewusst KEIN Networking und kein Component: ob ein Spieler
/// einen Skin freigeschaltet hat, gebraucht wird das nur beim EIGENEN Kauf/Ausrüsten in der Lobby;
/// die anderen Peers sehen ohnehin nur den tatsächlich AUSGERÜSTETEN Skin (den spiegelt
/// <see cref="HorseVisuals"/> per [Sync]). Der Stand wird in <see cref="FileSystem.Data"/> gespeichert
/// und übersteht damit einen Neustart – gleiches Muster wie der lokale Nickname
/// (<see cref="PlayerNameManager"/>) und der zuletzt gewählte Skin (<see cref="ChariotSkinPreference"/>).
///
/// Regeln:
///   - <see cref="ChariotSkin.Pferdekopf"/> ist der BASIS-Skin: immer freigeschaltet, kostet nichts.
///   - <see cref="ChariotSkin.Krokodilkopf"/> muss gekauft werden (siehe <see cref="ChariotSkinShop"/>).
///
/// Der PG-Abzug beim Kauf ist host-autoritativ (<see cref="ChariotSkinShop"/>); erst wenn der Host den
/// Kauf bestätigt hat, ruft er (auf dem Käufer) <see cref="MarkOwned"/> auf. Diese Klasse hält also nur
/// das Ergebnis fest, sie entscheidet NICHT über Preis oder PG.
/// </summary>
public static class ChariotSkinOwnership
{
	/// <summary> Datei in FileSystem.Data mit den gekauften (Nicht-Basis-)Skins dieses Rechners. </summary>
	public const string OwnedFile = "chariot_owned.txt";

	// null = noch nicht von der Platte geladen. Enthält NUR gekaufte Nicht-Basis-Skins; der
	// Basis-Skin (Pferdekopf) gilt immer als besessen und steht nie in der Datei.
	static HashSet<ChariotSkin> _owned;

	/// <summary> Ob dieser Skin dem lokalen Spieler gehört (Basis-Skin immer true). </summary>
	public static bool IsOwned( ChariotSkin skin )
	{
		if ( skin == ChariotSkin.Pferdekopf ) return true;   // Basis-Skin – immer freigeschaltet
		_owned ??= LoadFromDisk();
		return _owned.Contains( skin );
	}

	/// <summary>
	/// Skin als freigeschaltet merken (und auf Platte speichern). Für den Basis-Skin ein No-Op.
	/// Wird vom <see cref="ChariotSkinShop"/> aufgerufen, NACHDEM der Host die PG abgebucht hat.
	/// </summary>
	public static void MarkOwned( ChariotSkin skin )
	{
		if ( skin == ChariotSkin.Pferdekopf ) return;
		_owned ??= LoadFromDisk();
		if ( !_owned.Add( skin ) ) return;   // schon besessen -> nichts speichern
		SaveToDisk();
	}

	static HashSet<ChariotSkin> LoadFromDisk()
	{
		var set = new HashSet<ChariotSkin>();
		try
		{
			if ( FileSystem.Data.FileExists( OwnedFile ) )
			{
				foreach ( var token in (FileSystem.Data.ReadAllText( OwnedFile ) ?? "")
					.Split( new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries ) )
				{
					if ( Enum.TryParse<ChariotSkin>( token.Trim(), out var v ) && v != ChariotSkin.Pferdekopf )
						set.Add( v );
				}
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[ChariotSkinOwnership] Konnte '{OwnedFile}' nicht laden: {e.Message}" );
		}
		return set;
	}

	static void SaveToDisk()
	{
		try
		{
			FileSystem.Data.WriteAllText( OwnedFile, string.Join( ",", _owned.Select( s => s.ToString() ) ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[ChariotSkinOwnership] Konnte '{OwnedFile}' nicht speichern: {e.Message}" );
		}
	}
}
