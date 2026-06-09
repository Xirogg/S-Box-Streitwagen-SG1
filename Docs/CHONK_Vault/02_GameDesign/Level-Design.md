---
titel: Level Design
tags: [gamedesign, leveldesign, maps, producing]
status: living
quelle: Meetings 11–14 (Team AngryPike, 2026)
aktualisiert: 2026-06-09
---

# Level Design

Gemeinsame Verantwortung: **Michael Würfl (Producing)** + **Sandra Gehrke (Game Design)**, abgestimmt mit Emmeline Felsing (Asset-Integration). Erste Level-Design-Session beschlossen (Meeting 11; Meeting 13: Ägypten begonnen).

Übergeordnete Streckenziele: [[02_GameDesign/Strecken_und_Umwelt]].

---

## Map-Übersicht (Stand 09.06.2026)

### 🏺 Ägypten
| Element             | Status                                                             |
| ------------------- | ------------------------------------------------------------------ |
| Terrain / Elevation | ✅ implementiert (M13), Dünen                                       |
| 3D-Assets           | Palme ✅ , Zypresse ✅, Kakteen ✅ — **noch nicht auf Map platziert** |
| Weitere Assets      | ⬜ offen                                                            |

### 🏛️ Griechenland
| Element             | Status                                       |
| ------------------- | -------------------------------------------- |
| Terrain / Elevation | ⬜ **Update nötig** (M14), Hügel/Berge        |
| 3D-Assets           | Zypresse ✅ modelliert — noch nicht platziert |
| Weitere Assets      | ⬜ Tempel/Gebäude                             |

### 🏟️ Rom
| Element | Status |
|---|---|
| Terrain / Elevation | ⬜ **Update nötig** (M14) |
| 3D-Assets | ⬜ offen |
| Weitere Assets | ⬜ offen |

---

## Design-Ziele je Map (aus Strecken-Konzept)

- **Kompakte Layouts** — Renndauer ≤ 5 min, dichte Fahrlinien.
- **Elevation Changes** — Physik-Interaktion getestet (Verbesserung M14), weiteres Tweaking bis Playtesting 16.06.
- **Kurven-Qualität** — weiche Bögen statt harter Winkel; relevant für Drift- und Ruck-Mechanik.
- **Lesbarkeit im Rennen** — Streckenführung auf einen Blick erkennbar (Vision-Anker: Antike als Setting, nicht Simulation → [[02_GameDesign/Vision_und_Pillars]]).

---

## Asset-Anforderungen

| Asset | Bereit | Platziert |
|---|---|---|
| Palme | ✅ (UV ~) | ⬜ |
| Zypresse | ✅ | ⬜ |
| Kaktus | ⬜ | ⬜ |
| Weitere Vegetation / Architektur | ⬜ | ⬜ |

Platzierung ist Aufgabe von **Emmeline** (Tech Art / Map-Integration) — abgestimmt mit Level-Design-Session (Michael + Sandra).

---

## Offene Aufgaben

- [ ] Griechenland — Terrain Elevation Update
- [ ] Rom — Terrain Elevation Update
- [ ] Ägypten — Palmen, Kakteen platzieren
- [ ] Griechenland, Rom — Zypressen + weitere Assets platzieren
- [ ] Playtesting 16.06. — Strecken auf Fahrbarkeit und Lesbarkeit testen

## Verknüpft
- Streckenkonzept (Typen, Modi) → [[02_GameDesign/Strecken_und_Umwelt]]
- Elevation-Physics → [[03_Programming/Systemarchitektur]] · [[01_Producing/Bug-Tracking]]
- Asset-Pipeline → [[04_Art_TechArt/Asset-Pipeline]]
