---
titel: Performance-Ziele & Texturbudgets
tags: [art, techart, performance, texturen]
status: reviewed
quelle: TAD (Felsing, 2026); Tech Bible (Lange, 2026)
aktualisiert: 2026-05-30
---

# Performance-Ziele & Texturbudgets

## Ziel-Mindestanforderung (mit Programming abgestimmt)

| Komponente | Mindestanforderung |
|---|---|
| OS | Windows 10 (64-Bit) |
| CPU | Core i5-9600K / Ryzen 5 3600 |
| RAM | 16 GB |
| GPU | GTX 1060 / RX 580 (6 GB VRAM) |
| Speicher | 12 GB |
| Netzwerk | Breitband |

## Verbindliche Performance-Richtwerte

| Zielwert | Spezifikation |
|---|---|
| Framerate | 60 FPS @ FullHD |
| Frame Time | < 16 ms (1000 ms ÷ 60) |
| Draw Calls | < 500 |

Begründung: Zielgruppe sind Low-Budget-PCs/Laptops; gezielte technische Reduktion ist ein zentrales Produktionsmittel (TAD, Felsing, 2026).

## Polygon-Budgets

| Objektklasse | Budget | Beispiel |
|---|---|---|
| Environment | < 500 Polygone | Pyramide ~80 |
| wichtige Objekte | < 2000 Polygone | Streitwagen ~174 |

(Tech Bible, Lange, 2026)

## Texturen

- **Props & Environment:** gemeinsamer **Texture Atlas**, 4096 × 4096 px (Erhöhung von ursprünglich 2048², da Mobile nicht mehr Zielplattform → mehr Budget). Reduziert Draw Calls und Asset-Clutter.
- **Charaktere & Zugtiere:** je separate Textur, 2048 × 2048 px.
- **Für alle:** nur **Albedo** (+ Emissive falls nötig); **keine** Normal-/Roughness-/Metallic-Maps (Shader-bedingt).

(TAD, Felsing, 2026)

## Verknüpft
- Engine-Benchmarks → [[03_Programming/Performance-Benchmarks]]
- QM Performance → [[01_Producing/QM_Definition_of_Done]]
