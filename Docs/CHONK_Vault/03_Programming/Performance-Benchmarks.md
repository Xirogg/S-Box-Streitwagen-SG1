---
titel: Performance-Benchmarks
tags: [programming, performance, benchmarks, sbox]
status: reviewed
quelle: Tech Bible (Lange, 2026)
aktualisiert: 2026-05-30
---

# Performance-Benchmarks (s&box)

## Rahmenbedingungen

Ziel: gute Spielbarkeit auch auf Low-Performance-PCs/Laptops — im Friendslop-Genre eine der wichtigsten Anforderungen. Alle technischen Entscheidungen zielen auf ein **geringes Hardware-Profil** (Tech Bible, Lange, 2026).

- **Frame Rate:** 60 FPS (Ultra HD)
- **Frame Budget:** < 16 ms (1000 ms / 60)

## Draw Calls

| Draw Calls | Zusatz-Frametime |
|---|---|
| Baseline | 3–4 ms |
| 100 | +0,005 ms |
| 1.000 | +0,25 ms |
| 2.000 | +0,5 ms |
| 5.000 | +1,5 ms |

**Fazit:** Draw Calls dürften kaum Performance-Probleme bringen; limitierender Faktor eher CPU-/GPU-Bottleneck.

## Partikel

| Systeme (à 5k) | Zusatz-Frametime |
|---|---|
| Baseline | 3–4 ms |
| 1 (5k) | +0,05 ms |
| 2 (10k) | +0,10 ms |
| 4 (20k) | +0,20 ms |

Limitierender Faktor ist **Overdraw** durch übereinanderliegende transparente Sprites, nicht die Partikelzahl. **Produktionsrichtwert:** max. 4 simultane Particle Systems à 1.000 Partikeln (Headroom für Göttereffekte/Blitze).

## Polycount

| Asset | Polycount | Frametime |
|---|---|---|
| Baseline | 0 | 3–4 ms |
| 4× Chariot | 174 | 3–4 ms |
| 4× Chariot | 1.000 | 3–4 ms |
| 4× Chariot | 5.000 | 3–4 ms |
| 4× Chariot | 30.000 | — |

Erste messbare Steigerung ab **~4,77 Mio. Polygone** (4–5 ms). **Fazit:** s&box zeigt bei extremer Dichte recht früh Einbußen, ist für den geplanten Umfang aber performant genug; die Polycount-Budgets dienen v. a. der **Konsistenz** der Assetproduktion.

## Verknüpft
- Asset-Polygon-Budgets → [[04_Art_TechArt/Performance-Ziele]]
- QM Performance-Dimension → [[01_Producing/QM_Definition_of_Done]]
