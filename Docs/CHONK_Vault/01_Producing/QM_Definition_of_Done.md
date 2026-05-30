---
titel: Qualität & Definition of Done
tags: [producing, qm, qualität, dod]
status: reviewed
quelle: QM-Strategie Research Bible (Würfl, 2026); Progress Update Woche 4 (Würfl, 2026)
aktualisiert: 2026-05-30
---

# Qualität & Definition of Done (QM-Strategie)

## Was ist Qualität? — 6 Dimensionen

Direkt aus der Game Vision abgeleitet (QM-Strategie Research Bible, Würfl, 2026):

1. **Game Feel** — sofortige Reaktion, spürbares Gewicht; Drift & Götter-Fähigkeit fühlen sich kraftvoll an.
2. **Multiplayer-Stabilität** — zuverlässige Lobbies, Sync < 100 ms RTT, kein Desync bei Götter-Fähigkeiten.
3. **Performance** — min. 60 FPS auf GTX 1060 / Apple M1, 144 FPS auf moderner Hardware.
4. **Art-Konsistenz** — stilisierte Antike, klar lesbar im Rennen; Lesbarkeit vor Detailgrad.
5. **Audio-Atmosphäre** — Proximity-Voice-Chat als Kernfeature; Musik passt sich der Rennposition an.
6. **Onboarding** — erstes Rennen < 5 Min ohne Tutorial („einsteigen, nicht durchlesen").

## Definition of Done — 3 Stufen

(QM-Strategie Research Bible, Würfl, 2026; angelehnt an Keith, 2013; Politowski et al., 2021)

| Stufe | Kriterium |
|---|---|
| **1 — Prototype Done** | Mechanik spielbar, kein finales Art. Zweck: Idee validieren. (z. B. Götter-Fähigkeit feuert, hat Cooldown, kein VFX nötig.) |
| **2 — Sprint Done** | In Engine, Multiplayer synchronisiert, Code-Review + Smoke-Test bestanden, Build crasht nicht. |
| **3 — Done-Done** | Final Art + SFX/VFX, balanced, dokumentiert, performance-getestet, QA-Checkliste vollständig. |

Ergänzend: 8 Feature-, 8 Level- (inkl. 60-FPS-Min-Spec) und 8 Asset-Kriterien (inkl. Git LFS & Art Bible).

## Testing-Systematik

(Unity Technologies, o. J.; Sloyd, 2025)

- **Unit Tests:** Mechanik-Logik, Damage-Berechnung, Cooldown-Timer — automatisiert im CI.
- **Integration Tests:** Netcode-Sync, Lobby-Setup, Multiplayer-State — wöchentlich vor Build.
- **Wöchentliche Playtests:** 2–4 Spieler, moderiert, Fokus pro Sprint, mit Beobachtungsprotokoll.
- **Stage-Gate-Playtest:** vollständiges 5-Min-Rennen gegen alle fünf Vision-Anker (Go/No-Go).

## Bug-Severity

| Stufe | Frist | Beispiel |
|---|---|---|
| CRITICAL | < 24 h | Crash, Data-Loss, Multiplayer-blockierend |
| MAJOR | < 1 Sprint | Feature kaputt, Workaround vorhanden |
| MINOR | < 2 Sprints | kosmetisch, kein Spielfluss-Impact |
| TRIVIAL | Backlog | Tippfehler, Polish, Nice-to-have |

## Verknüpft
- 5 Vision-Anker → [[02_GameDesign/Vision_und_Pillars]]
- Performance-Ziele Tech Art → [[04_Art_TechArt/Performance-Ziele]]
- Benchmarks Programming → [[03_Programming/Performance-Benchmarks]]
