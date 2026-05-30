---
titel: Götter-System
tags: [gamedesign, götter, mythologie, wirtschaft]
status: reviewed
quelle: GDD (Gehrke, 2026)
aktualisiert: 2026-05-30
---

# Götter-System

Vier Götter aus vier antiken Kulturen, je mit Mythos-Hintergrund. Die Darstellung lehnt sich an die rundlichen Base-Charaktere an (2D, bewusster Stilbruch; erscheinen für alle sichtbar am Himmel der Strecke).

| Gott | Kultur | Domäne | Mythos-Bezug |
|---|---|---|---|
| **Maat** | Ägyptisch | Ordnung & Gleichgewicht | universelle Ordnung; gleiche Gerechtigkeit vor Gericht (Felske, o. J.) |
| **Taranis** | Keltisch | Wetter & Donner | Himmelsgott, oft mit Blitzbündel/Rad dargestellt (Schmudlach, 2010) |
| **Dionysos** | Griechisch | Wein, Wahnsinn, Ekstase | wandelt feiernd durch die Welt (Korzonnek, 2023) |
| **Laverna** | Römisch | Diebstahl & Betrug | Schutzpatronin der Diebe (Hederich, 1770; Vollmer, 1874) |

→ Fähigkeiten je Gott: [[02_GameDesign/Item-Fähigkeiten]]

## Item-Fähigkeiten: Normal vs. Göttlich

- **Normal:** betrifft i. d. R. 1–3 Spieler bzw. die in unmittelbarer Nähe; geringerer Einfluss auf die Platzierung.
- **Göttlich (Ulti):** beeinflusst **alle** Spieler; ruft den Gott physisch in die Welt. Mehrere gleichzeitig gerufene Götter erscheinen verkürzt nacheinander; alle Effekte lösen wie geplant aus.

## Opfer-Altar

Publikumsgunst gegen **Götter-Gunst** fürs nächste Rennen eintauschen — Angebot ist **zufällig** (roguelike-artig) und host-/spielerabhängig.

- Das Angebot wird nach **erstmaligem Start** des aktuellen Rennens festgelegt und erst **nach Abschluss** neu gezeigt; ein Spielabbruch erlaubt **kein** „Neu-würfeln".
- **Auswahlreihenfolge:** erstes gemeinsames Rennen „wer zuerst kommt, mahlt zuerst"; danach wählen die **Verlierer zuerst** (Letzter → Vorletzter → …).

### Götter-Gunst
Eine garantierte Normal-Fähigkeit zu Rennbeginn **oder** eine erhöhte Chance (5–20 %), einmalig eine bestimmte Götter-Fähigkeit zu erhalten. Effekt endet mit dem Rennen.

### Götter-Fluch
Wer **mehr als eine** Gunst kauft, riskiert den Zorn der Götter: je weitere Gunst **50 % Fluch-Chance**. Fluch = **Kopfgeld** + Verlust der erkauften Vorteile (kein Geld zurück). Account-spezifischer Zustand, verschwindet erst nach Fahren einer Runde. Zweck: verhindert, dass reiche Langzeitspieler dauerhaft zu großen Vorteil haben.

### Kopfgeld
Andere Spieler erhalten **mehr Publikumsgunst**, wenn sie einem Kopfgeld-Spieler Schaden zufügen. Betroffene werden zu Rennbeginn angekündigt.

## Random Events
Pro Checkpoint und Spieler eine prozentuale Chance auf einen zufälligen Göttereingriff auf der Strecke (z. B. Dionysos lässt eine **Riesentraube** fallen, der man ausweichen muss; Treffer = Schaden).

## Verknüpft
- Meta-Station UI → [[02_GameDesign/Meta-Loop_Stationen]]
- Wirtschaftslogik → [[05_Marktforschung/Geschäftsmodell_Preisstrategie]]
