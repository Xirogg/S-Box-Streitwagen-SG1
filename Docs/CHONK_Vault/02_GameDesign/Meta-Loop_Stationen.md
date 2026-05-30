---
titel: Meta-Loop-Stationen / UI
tags: [gamedesign, meta, ui, hub]
status: reviewed
quelle: GDD (Gehrke, 2026)
aktualisiert: 2026-05-30
---

# Meta-Loop-Stationen / UI

Die Vorbereitungs- und Erholungsphase ([[02_GameDesign/Game-Loop]]) laufen über fünf Stationen. Aktuell als **UI** umgesetzt; in späteren Iterationen als physische Lobby mit je einem **HUB-NPC** pro Station (1–2 kurze Interaktionen, Text und/oder Voicelines).

| Station | Funktion | kosmetisch / Stats |
|---|---|---|
| **Charakter-Erstellung** | Charakter-Teile kaufen/auswählen | kosmetisch |
| **Chimären-Stall** | Zugtier-Teile kaufen/auswählen | kosmetisch → [[02_GameDesign/Chimären-System]] |
| **Streitwagen-Werkstatt** | Wagen kaufen/auswählen **+ Reparatur** | **Stats** (Geschwindigkeit, Gewicht) |
| **Opfer-Altar** | Gunst gegen Götter-Gunst (Zufallsangebot) | Risiko: Götter-Fluch → [[02_GameDesign/Götter-System]] |
| **Rennstrecken-Wahl** | Strecke/Wettkampf auswählen | → [[02_GameDesign/Strecken_und_Umwelt]] |

## Währung & Freischaltung

- **Kaufbar mit Publikumsgunst:** Kosmetik (Chimären-, Charakter-Teile), Streitwägen + Reparatur, Götter-Gunst.
- **Progression durch Erfahrung** (gesamte je gesammelte Gunst): Rennstrecken inkl. zugehöriger Random-Events & beeinflussbarer Umwelt.

(GDD, Gehrke, 2026)

## In-Game-Interface (Auszug)

3rd-Person-Follow-Kamera mit FOV-Weitung bei Top-Speed; HUD zeigt u. a. Gunst-Anzeige und aktive Items. Farbpalette mit Adobe Color erstellt; Schriftart u. a. *Macondo* (Google Fonts) für den antik-mittelalterlichen Look (GDD, Gehrke, 2026; Google, o. J.; Adobe, o. J.).

## Verknüpft
- NPCs/Götter → [[02_GameDesign/Götter-System]]
- UI-Lesbarkeit als QM-Dimension → [[01_Producing/QM_Definition_of_Done]]
