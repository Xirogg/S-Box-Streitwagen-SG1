---
titel: Wagen-Stats & Balancing
tags: [gamedesign, balancing, stats, wagen, draft]
status: draft
quelle: Balancing-Tabelle (tabelle.pdf, Team AngryPike, 2026); Meetings 09–12
aktualisiert: 2026-05-30
---

# Wagen-Stats & Balancing

> [!warning] Lebendes Balancing-Dokument
> Werte sind **work in progress** und müssen per Playtesting verifiziert werden (Meetings 11 & 12). Drift- und Gewichts-Mapping sind noch „Theorie" und brauchen Direktvergleich im Team (Balancing-Tabelle, 2026).

## Sechs Spieler-Stats

Jeder Wagen hat sechs Stats auf einer Skala (Presets nutzen **1–6**, Basis = 3):
**Tempo · Handling · Drift · Gewicht · Angriff · Robustheit.**

## Wagen-Presets

| Typ | Tempo | Handling | Drift | Gewicht | Angriff | Robustheit | Spielgefühl |
|---|---|---|---|---|---|---|---|
| **Allrounder (Basis)** | 3 | 3 | 3 | 3 | 3 | 3 | Ausgewogen, verzeiht Fehler. Ideal für Einsteiger. |
| **Der Sprinter** | 6 | 3 | 2 | 2 | 2 | 3 | Extrem schnell auf Geraden, verliert in engen Kurven leicht an Boden. |
| **Der Kurvenkönig** | 3 | 6 | 5 | 1 | 1 | 2 | Extrem agil, zieht präzise Linien, anfällig gegen Rammattacken. |
| **Der Panzer** | 2 | 2 | 1 | 6 | 2 | 5 | Träge Beschleunigung, schiebt im Getümmel alles beiseite. Zäh. |
| **Der Gladiator** | 4 | 2 | 2 | 3 | 5 | 2 | Offensiver Ramm-Wagen. Teilt extremen Schaden aus, solider Grundspeed. |
| **Der Drifter** | 4 | 2 | 6 | 2 | 2 | 2 | Powerslide-Spezialist. Top-Speed primär durch sauberes Driften. |
| **Die Glaskanone** | 5 | 4 | 2 | 1 | 5 | 1 | Blitzschnell und tödlich im Angriff, hält kaum Treffer aus. |

## Stat → Backend-Variable (Stat-Stufe 1 → 13)

| Spieler-Stat | Backend-Variable | Werteverlauf (Stufe 1 … 13) |
|---|---|---|
| **(1) Tempo** | `Pullforce` | 4500, 5000, 5500, 6000, 6500, 7000, 7500, 8000, 9000, 9500, 10000, 10500, 11000 |
| | `MaxAngularSpeed` | 50, 100, 150, 200, 250, 300, 400, 500, 600, 700, 800, 900, 1000 |
| | `Steer Torque` | 1000 (konstant) · `Brake Force` 5 (konstant) |
| **(2) Handling** | `LateralGrip (wagen)` | 0, 3, 6, 9, 12, 15, 18, 21, 24, 27, 30 (danach Kappung bei 30) |
| | `ChariotAngularDamping` | 0.60, 1.2, 1.8, 2.4, 3.0, 3.6, 3.8, 4.0, 4.2, 4.4, 4.6, 4.8 |
| **(3) Drift** | `DriftForce` / `Drift Rear Offset` / `Drift Speeds` | **0** (noch nicht implementiert — „Noch Theorie") |
| **(4) Gewicht** | `Mass` | **0** (Norm aktuell: **55**) — Mapping offen |
| **(5) Angriff** | `Base Value Attack` | 60, 80, 100, 130, 170, 220, 280, 350, 430, 520, 630, 760, 950 |
| | `Base Value Defense` | 0, 15, 30, 45, 65, 85, 110, 140, 175, 215, 260, 315, 380 |
| **(6) Robustheit** | `MaxHP` | 1000, 1150, 1300, 1500, 1750, 2000, 2250, 2500, 2800, 3100, 3400, 3750, 4200 |

## Combat-Referenz (gegen Basis-Gegner)

**Angriff → Treffer, um einen Basis-Gegner zu zerstören:**
1 → 29 · 2 → 22 · 3 (Basis) → 17 · 4 → 13 · 5 → 10 · 6 → 8

**Robustheit → Treffer, bis man selbst zerstört wird:**
1 → 10 · 2 → 14 · 3 (Basis) → 17 · 4 → 22 · 5 → 29 · 6 → 37

**Theorie „Gleich trifft auf Gleich":** Stufe 1–7 = 17 Hits, dann 18, 18, 19, 20, 21, 22.

## Offene Punkte / Anmerkungen

- **Drift-Mapping** noch offen: Frage an Jonathan zum Unterschied bei Q/E-Eingabe (ggf. Faktor ×1000). Q/E-Driftwert funktioniert aktuell **nicht** (Meeting 12).
- **Gewicht** soll laut Meeting 11/12 in der Wirkung **auf Combat beschränkt** werden, um das Handling nicht zu beeinträchtigen.
- Punkteverteilung der Presets evtl. enger fassen, falls so beibehalten.

## Verknüpft
- Physik-Formeln (Wucht, Fliehkraft) → [[02_GameDesign/Steuerung_und_Physik]]
- Build-Reihenfolge (Stats zuerst) → [[03_Programming/Systemarchitektur]]
- Combat/Rammen → [[02_GameDesign/Game-Loop]]
