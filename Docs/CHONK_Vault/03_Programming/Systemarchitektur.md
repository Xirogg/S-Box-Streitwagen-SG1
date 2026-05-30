---
titel: Systemarchitektur & Build-Reihenfolge
tags: [programming, architektur, ordnerstruktur, buildorder]
status: reviewed
quelle: Tech Bible (Lange, 2026); Prototyp-Stand (Team AngryPike, 2026)
aktualisiert: 2026-05-30
---

# Systemarchitektur & Build-Reihenfolge

## Aktuelle Build-Reihenfolge (Stand Meeting 09, 20.05.2026)

Nach dem Engine-Verbleib bei s&box und dem Prof-Feedback wurde die Reihenfolge auf **„Stats & Player Prefab zuerst"** umgestellt (Meeting 09):

1. **Player & Stats** — Stat-Verteilungen für Wagen: **Gewicht, Max Speed, Attack, Defense** (→ später erweitert auf 6 Stats: Tempo, Handling, Drift, Gewicht, Angriff, Robustheit). → [[02_GameDesign/Wagen-Stats_Balancing]]
2. **Balancing des Fahrgefühls mit Gewicht** — prototypisch in Engine mit Beispielen + Playtesting.
3. **Optische Aufwertung des Player-Prefab** — Pferde-Model mit Animation, Wagen, Chohonk.
4. **Strukturierung in Codecks.**

Danach: (1) **Combat mit Rammen** → (2) **Götterfähigkeiten** → (3) **Opferaltar** → (4) **Chimären-System**.

**Umsetzungsstand (Meetings 11–12, 28.–30.05.):** Currency-Manager (Münzen), Combat-Bonus, Kopfgeld und Konto-Persistenz implementiert; Item-System (Mario-Kart-artig) + UI-Anzeige in Arbeit; Q/E-Driftwert noch fehlerhaft; Bewegungs-Bugfix offen; Fähigkeiten brauchen individuelle HitReg.

## Ursprüngliche Build-Reihenfolge (Prototyp, historisch)

1. **ChariotController** — Physik. → [[02_GameDesign/Steuerung_und_Physik]]
2. **Multiplayer-Session-Management** — Lobby, Ready/Countdown, State-Sync.
3. **Ramming-System** — Kollision/Wucht, Schaden nach ATK-Stat, Trümmer.
4. **God-Ability-Architecture** — Trigger via B, Cooldown, Ziel-Selektor. → [[02_GameDesign/Item-Fähigkeiten]]
5. **Arena / Win-Conditions** — Checkpoints, Platzierung, Random-Events, Ziellogik.

Erste s&box-Prototypen zu **Rammen, Kollision und Kamera-FOV** liegen vor (Progress Update, 30.04.2026).

## Ordnerstruktur (Guidelines)

- Selbstverständliche, sprechende Ordnernamen; anhand des obersten Ordners muss sofort klar sein, was er enthält.
- Oberordner farblich markieren; neue Unterordner nur in Absprache mit dem Tech Lead.
- Beispiel-Top-Level: `Scripts`, `Materials`, … (Tech Bible, Lange, 2026).

## Szenenstruktur

- GameObjects **nie** lose in die Szene — immer unter den richtigen **Parent**.
- Runtime-Instanziierungen an einen Parent hängen, nicht in den Szenen-Root spawnen.
- GameObjects klar und **auf Englisch** benennen.

## Dateiformate

| Asset | Format |
|---|---|
| Textures / UI | `.png` oder `.jpg` |
| SFX | `.wav` |
| Musik | `.mp3` oder `.wav` |
| 3D-Modelle / Animationen | `.fbx` |

> Engine-Dateien von s&box selbst werden **nicht** verändert (Tech Bible, Lange, 2026).

## Langfristige Architektur vs. kurzfristige Produktivität
Wegen des Zeitdrucks gilt: **wichtige Skripte müssen sauberen Code** haben, alles darüber ist Bonus. Für die wichtigsten Skripte wird eine **Mindmap in Codecks** geführt (was langfristig passt / was zu überarbeiten ist).

## Verknüpft
- Engine-Spezifika → [[03_Programming/Tech-Stack_und_Engine]]
- Benchmarks → [[03_Programming/Performance-Benchmarks]]
