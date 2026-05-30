---
titel: Item-Fähigkeiten (8 Items)
tags: [gamedesign, items, fähigkeiten, balancing]
status: reviewed
quelle: GDD (Gehrke, 2026)
aktualisiert: 2026-05-30
---

# Item-Fähigkeiten — 4 Götter × 2 Fähigkeiten

Im aktuellen Umfang: **4 Götter mit je 2 Fähigkeiten = 8 Fähigkeiten**, je eine **Göttliche (Ulti)** und eine **Normale**. Auslösung mit **B**. Umfang erweiterbar je nach Nachfrage/Möglichkeiten (GDD, Gehrke, 2026).

## Taranis (Blitze & Wetter)

| | Göttlich: **Götterfunke** | Normal: **Blitzbombe** |
|---|---|---|
| Logik | Tempo- & Schadenswert bei **allen** erhöhen; Auslöser auf *unverwundbar* setzen | kurzer Timer (Ausweichchance), Radius-Abfrage um den Wagen, allen getroffenen Objekten HP abziehen |
| Dauer | nach Zeit X Werte zurücksetzen | einmalig (Sofort-Effekt) |

## Maat (Karma / Gleichgewicht)

| | Göttlich: **Das jüngste Gericht** | Normal: **Karma-Schild** |
|---|---|---|
| Logik | Ranking prüfen: Tempo bei vorderen Plätzen senken, bei hinteren erhöhen | eingehender Schaden wird direkt beim **Verursacher** abgezogen |
| Dauer | nach Zeit X zurücksetzen | nach Zeit X deaktivieren |

## Dionysos (Wahnsinn)

| | Göttlich: **Trunkenheit am Steuer** | Normal: **Trauben-Schütze** |
|---|---|---|
| Logik | Lenkungs-Input bei allen außer Auslöser umkehren; Bildschirm-Filter bei allen außer Auslöser | 5 Objekte mit Vorwärtskraft + Zufallswinkel erzeugen; Abprall-Physik an Wänden |
| Dauer | nach Zeit X zurücksetzen | 5 Schuss; Geschosse verschwinden bei Treffer oder nach Zeit X |

## Laverna (Stehlen / Betrug)

| | Göttlich: **Göttlicher Hehler** | Normal: **Item-Dieb** |
|---|---|---|
| Logik | HP-Differenz bei anderen abziehen und dem Spieler gutschreiben (von allen, die mehr haben, mit Vorrang der Reichsten) | zufälligen Spieler wählen (nicht Letzter); Item an Auslöser übertragen (95 %) oder löschen (5 %) |
| Dauer | einmalig (Sofort-Effekt) | einmalig (Sofort-Effekt) |

---

> [!tip] Architektur-Hinweis (Programming)
> Diese Programmierlogik ist die Vorlage für die **God Ability Architecture** (Build-Priorität 4, [[03_Programming/Systemarchitektur]]). Empfehlung: gemeinsame Ability-Basis (Trigger, Cooldown, Ziel-Selektor: self / radius / all-except-self / ranking-based), Netzwerk-Effekte serverautoritativ. Vgl. Jupiter-Blitz-Referenz (Mario-Kart-artige Homing-Projektile).

## Verknüpft
- Götter & Opfer-Altar → [[02_GameDesign/Götter-System]]
- Comeback-Balancing → [[02_GameDesign/Game-Loop#3-core-renn-phase]]
