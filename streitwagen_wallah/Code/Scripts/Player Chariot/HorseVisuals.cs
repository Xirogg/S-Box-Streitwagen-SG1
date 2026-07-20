using Sandbox;
using System;
using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Steuert den Wagen-Skin (Pferdekopf / Krokodilkopf) für BEIDE Pferde des Streitwagens.
///
/// Beide Köpfe stecken im selben Modell (<c>m_cherd_rig_2.vmdl</c>); der Krokodilkopf ist ein
/// separates Submesh ("KrokoKopfSeperat"). Umgeschaltet wird NICHT das Modell, sondern nur eine
/// Body-Group im Modell:
///   - <see cref="ChariotSkin.Krokodilkopf"/>: KrokoKopfSeperat sichtbar
///   - <see cref="ChariotSkin.Pferdekopf"/>:   KrokoKopfSeperat ausgeblendet
///
/// Ablauf: Der Spieler wählt den Skin in der Lobby (Button -> <see cref="ChariotSkinPreference"/>).
/// Beim Spawn des Wagens liest der BESITZER seine Wahl und spiegelt sie per [Sync] an alle Peers,
/// damit jeder denselben Skin sieht (host-/besitzer-autoritativ, siehe [[project-overview]]).
///
/// VORAUSSETZUNG im Modell: In ModelDoc muss eine Body-Group mit dem Namen
/// <see cref="CrocodileHeadBodyGroup"/> existieren, deren eine Auswahl den KrokoKopfSeperat-Submesh
/// zeigt und die andere ihn weglässt. Fehlt sie, bleibt die Umschaltung wirkungslos (einmalige
/// Warnung im Log) – Namen/Auswahl-Indizes sind unten im Editor einstellbar.
/// </summary>
public sealed class HorseVisuals : Component
{
	/// <summary> Die beiden aktiven Pferd-Nodes (die horse-Prefab-Instanzen). Im Editor zuweisen. </summary>
	[Property, Group( "Skin" )]
	public List<GameObject> HorseNodes { get; set; } = new();

	/// <summary>
	/// Kombiniertes Pferde-Modell mit beiden Köpfen (<c>m_cherd_rig_2.vmdl</c>). Wird – falls gesetzt –
	/// auf beide Pferd-Renderer angewendet, sodass garantiert BEIDE dasselbe Modell nutzen (die beiden
	/// Nodes stehen sonst evtl. auf unterschiedlichen Modellen). Echte Asset-Referenz, KEIN String-Pfad.
	/// </summary>
	[Property, Group( "Skin" )]
	public Model HorseModel { get; set; }

	/// <summary> Name der Body-Group im Modell, die das KrokoKopfSeperat-Submesh schaltet. </summary>
	[Property, Group( "Skin" )]
	public string CrocodileHeadBodyGroup { get; set; } = "KrokoKopfSeperat";

	/// <summary> Body-Group-Auswahl, bei der der Krokodilkopf SICHTBAR ist. </summary>
	[Property, Group( "Skin" )]
	public int CrocodileHeadShownChoice { get; set; } = 0;

	/// <summary> Body-Group-Auswahl, bei der der Krokodilkopf AUSGEBLENDET ist. </summary>
	[Property, Group( "Skin" )]
	public int CrocodileHeadHiddenChoice { get; set; } = 1;

	/// <summary> Aktueller Skin – besitzer-autoritativ gesetzt, an alle Peers gespiegelt. </summary>
	[Sync] public ChariotSkin Skin { get; set; }

	// Zuletzt tatsächlich auf die Renderer angewandter Skin (null = noch nie). Verhindert unnötige
	// Re-Applies pro Frame; bleibt null, solange die Modelle noch nicht geladen sind.
	ChariotSkin? _applied;
	bool _warnedMissingBodyGroup;

	protected override void OnStart()
	{
		// Nur der Besitzer bestimmt seinen eigenen Skin; [Sync] verteilt ihn an alle.
		if ( Network.IsOwner )
			Skin = ChariotSkinPreference.Current;

		ApplySkin();
	}

	protected override void OnUpdate()
	{
		// Besitzer: falls sich die Wahl ändert (z. B. nachträglich), übernehmen.
		if ( Network.IsOwner && Skin != ChariotSkinPreference.Current )
			Skin = ChariotSkinPreference.Current;

		// Alle Peers: neu anwenden, wenn sich der (ggf. via [Sync] empfangene) Skin geändert hat.
		if ( _applied != Skin )
			ApplySkin();
	}

	void ApplySkin()
	{
		int choice = Skin == ChariotSkin.Krokodilkopf ? CrocodileHeadShownChoice : CrocodileHeadHiddenChoice;

		int rendererCount = 0;   // gefundene Renderer/Props gesamt
		int modelReadyCount = 0; // davon mit bereits geladenem Modell
		bool bodyGroupFound = false;

		foreach ( var node in HorseNodes )
		{
			if ( node is null ) continue;

			// SkinnedModelRenderer erbt von ModelRenderer -> beide werden hier abgedeckt.
			foreach ( var renderer in node.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			{
				rendererCount++;
				if ( HorseModel is not null && renderer.Model != HorseModel )
					renderer.Model = HorseModel;

				if ( renderer.Model is null ) continue;
				modelReadyCount++;
				renderer.BodyGroups = BuildMask( renderer.Model, choice, ref bodyGroupFound );
			}

			// Auf beiden Pferd-Nodes liegt zusätzlich ein (Alt-)Prop-Renderer desselben Modells.
			// Damit die Umschaltung sichtbar ist, egal welcher Renderer gerade zeichnet, wird er
			// mit derselben Maske versorgt.
			foreach ( var prop in node.Components.GetAll<Prop>( FindMode.EverythingInSelfAndDescendants ) )
			{
				rendererCount++;
				if ( HorseModel is not null && prop.Model != HorseModel )
					prop.Model = HorseModel;

				if ( prop.Model is null ) continue;
				modelReadyCount++;
				prop.BodyGroups = BuildMask( prop.Model, choice, ref bodyGroupFound );
			}
		}

		// Erst als angewandt merken, wenn mind. ein Modell geladen ist (oder es gar keine Renderer
		// gibt) – sonst probiert es OnUpdate im nächsten Frame erneut.
		if ( rendererCount == 0 || modelReadyCount > 0 )
		{
			_applied = Skin;

			if ( modelReadyCount > 0 && !bodyGroupFound && !_warnedMissingBodyGroup )
			{
				_warnedMissingBodyGroup = true;
				Log.Warning( $"[HorseVisuals] Body-Group '{CrocodileHeadBodyGroup}' im Modell nicht gefunden – " +
					"Skin-Umschaltung bleibt wirkungslos. In ModelDoc eine Body-Group mit diesem Namen anlegen " +
					"(eine Auswahl MIT, eine OHNE KrokoKopfSeperat)." );
			}
		}
	}

	// Baut die Mesh-Group-Maske (BodyGroups) für die gewünschte Auswahl der Kopf-Body-Group.
	// Setzt bodyGroupFound=true, sobald die benannte Body-Group im Modell existiert.
	ulong BuildMask( Model model, int choice, ref bool bodyGroupFound )
	{
		ulong mask = model.DefaultBodyGroupMask;

		foreach ( var part in model.BodyParts )
		{
			if ( !string.Equals( part.Name, CrocodileHeadBodyGroup, StringComparison.OrdinalIgnoreCase ) )
				continue;

			bodyGroupFound = true;
			mask &= ~part.Mask;                          // Bits dieser Body-Group zunächst löschen …
			var choices = part.Choices;
			if ( choice >= 0 && choice < choices.Count )  // … und die gewünschte Auswahl setzen.
				mask |= choices[choice].Mask;
			break;
		}

		return mask;
	}
}
