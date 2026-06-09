---
titel: Asset-Pipeline & Workflows
tags: [art, techart, pipeline, workflow]
status: reviewed
quelle: Tech Bible (Lange, 2026); TAD (Felsing, 2026)
aktualisiert: 2026-06-09
---

# Asset-Pipeline & Workflows

## Pipeline-Prinzip (Guidelines)

Bei Namensgebung hat das Technical Department die letzte Entscheidung (in Absprache mit Art). Alle Assets gehen auch an den **Producer**: Producing beurteilt **Qualität**, Tech prüft **Dateigrößen/-formate** (Tech Bible, Lange, 2026).

## 2D-Asset-Workflow

| # | Prozess | Tool |
|---|---|---|
| 1 | Illustration | Procreate / Photoshop |
| 2 | Colouring | Procreate |
| 3 | Textures | Substance Painter |
| 4 | Export | Codecks / Discord |

Dateigrößen ≤ **5 MB** pro `.jpg`/`.png`.

## 3D-Asset-Workflow

| # | Prozess | Tool |
|---|---|---|
| 1 | Modelling | 3ds Max |
| 2 | UV-Unwrapping | 3ds Max |
| 3 | Texturing | Substance Painter |
| 4 | Animation | s&box |
| 5 | Import | s&box |

Grober Ablauf: grobes Asset erstellen → in Engine importieren & testen → vollständig produzieren → in den richtigen Ordner ziehen & implementieren.

## Eingesetzte Tools (TAD)

3ds Max / **Maya** (3D-Modelling — Meeting 13: Maya als neues Programm eingeführt), Photoshop (2D), Engine: **s&box** (entschieden 18.05.2026). Auswahl basiert auf Teamkompetenz und praktisch validierten Prototypen (TAD, Felsing, 2026; Meeting 13).

> [!note] Prototyp-Protokoll Nr. 7 — Modulares Mesh-Swap-System
> Beim Test des Chimären-Mesh-Swaps verarbeitete **Unity** das System sofort korrekt (~10 Min), während **s&box** fehlerbehaftet und zeitintensiv war (~90 Min, ungelöst: Animation nicht auf Swap-Meshes übertragen). Das stärkt die laufende Reevaluation zugunsten Unity (TAD, Felsing, 2026). → Engine-Frage: [[03_Programming/Tech-Stack_und_Engine]]

## 3D-Asset-Fortschritt (Stand 09.06.2026)

| Asset                 | Modelliert | UV                | Rigged/Skinned | Implementierbar      |
| --------------------- | ---------- | ----------------- | -------------- | -------------------- |
| Pferd                 | ✅          | ✅                 | ✅ (M14)        | **✅ ja**             |
| Streitwagen (Chariot) | ✅          | ✅ (M13)           | —              | 🔄 (Texturen fehlen) |
| Palme                 | ✅(M14)     | ~ (nicht perfekt) | —              | ✅                    |
| Zypresse              | ✅(M14)     | ~                 | —              | ✅                    |
| Kaktus                | ✅(M13)     | —                 | —              | ✅                    |

→ Map-Platzierung & Level-Design-Koordination: [[02_GameDesign/Level-Design]]

## Verknüpft
- Polygon-Budgets & Texturen → [[04_Art_TechArt/Performance-Ziele]]
- Benennung → [[04_Art_TechArt/Namenskonvention]]
- Engine-Performance → [[03_Programming/Performance-Benchmarks]]
