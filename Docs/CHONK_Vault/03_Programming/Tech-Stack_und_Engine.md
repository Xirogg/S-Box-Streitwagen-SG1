---
titel: Tech-Stack & Engine (s&box)
tags:
  - programming
  - engine
  - sbox
  - tech
status: reviewed
quelle: Tech Bible (Lange, 2026); Facepunch Studios (o. J.); TAD (Felsing, 2026)
aktualisiert: 2026-05-30
---

# Tech-Stack & Engine

## Plattform & Engine

- **Plattform:** PC; Maus & Tastatur oder Controller.
- **Engine:** **s&box** (Source-2-basiert, Facepunch) mit **C#**. Begründung: starke 3D-Fähigkeiten + Steam-integrierter Multiplayer für schnelle Entwicklung (Tech Bible, Lange, 2026; Facepunch Studios, o. J.).
- **Hauptanforderungen:** funktionaler Multiplayer · Physiksystem · Kollisionssystem · klare UI · gutes Networking.

> [!success] Engine-Entscheidung getroffen: **s&box** (Stand 18.05.2026)
> Die Engine-Frage wurde intensiv diskutiert und ist **entschieden**:
> - **Meeting 07 (16.05.):** Engine-Pivot s&box → Unity vorgeschlagen (Chimären-Mechanik in Unity reibungsloser, bessere Doku/Tutorials, Team vertraut). Gegen Unity: s&box hat eingebauten Multiplayer, Voice-Chat, bessere Kompilierungszeit, Steam-Integration und royalty-free Steam-Publishing. → vertagt.
> - **Meeting 08 (18.05.):** **Entscheidung: s&box.** 3D-Skin-to-Mesh-Bodygrouping ist in s&box möglich (wenn auch aufwändig).
>
> **Historischer Kontext (überholt):** Das **TAD** (Felsing, 2026) und das Prototyp-Protokoll Nr. 7 favorisierten zwischenzeitlich Unity (Mesh-Swap dort ~10 Min vs. s&box ~90 Min, ungelöst). Diese Dokumente **datieren vor** der finalen Entscheidung. → Hintergrund: [[04_Art_TechArt/Asset-Pipeline]], [[01_Producing/Projekthistorie]].

## s&box-Spezifika (für sauberen Code essenziell)

s&box nutzt ein **Component-System** (GameObject + Components), **nicht** das Legacy-Entity-System der älteren Source-Engine. Alle Skripte sind Components, deren Lebenszyklus-Methoden überschrieben werden (Facepunch Studios, o. J.).

**Networking-Attribute (serverautoritativ denken):**
- `[Sync]` — Property-Wert über das Netzwerk synchronisieren.
- `[Broadcast]` — Methode an alle Clients senden.
- `[Authority]` — Ausführung auf den autoritativen Besitzer beschränken.

**Input:** s&box-eigene APIs verwenden (z. B. `Input.AnalogMove` für WASD), **nicht** generische Unity-/Source-Muster.

**Lebenszyklus (Komponenten):** `OnAwake()` → `OnStart()` → `OnUpdate()` (siehe [[03_Programming/Code-Konventionen]]).

> [!info] Quellenlage
> Die offizielle s&box-Dokumentation (Facepunch Studios, o. J.) und der community-eigene SubZero-Guide (2025) sind die Referenz. Engine-Dateien von s&box selbst werden **nicht verändert** (Tech Bible, Lange, 2026).

## Tools im Programming-Workflow

- **GitHub** für Versionskontrolle ([[03_Programming/Version-Control]]).
- **Codecks** für Tasks/Mindmaps der wichtigsten Skripte; Discord-Bot-Benachrichtigung beim Starten einer Karte (Tech Bible, Lange, 2026).

## Verknüpft
- Build-Reihenfolge & Architektur → [[03_Programming/Systemarchitektur]]
- Physik-Formeln (ChariotController) → [[02_GameDesign/Steuerung_und_Physik]]
- Ability-Architektur → [[02_GameDesign/Item-Fähigkeiten]]
