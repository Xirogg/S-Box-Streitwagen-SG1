---
titel: Game-Loop (Langformat, aktuell)
tags: [gamedesign, gameloop, core, final]
status: reviewed
quelle: GDD (Gehrke, 2026) + aktualisiertes Langformat (Team AngryPike, 2026)
aktualisiert: 2026-05-30
---

# 🔁 Game-Loop — Streitwagen-Simulator (Langformat)

> **Aktueller, ausführlicher Stand des Game-Loops.** Er erweitert die Kurzfassung des GDD („Fahren → Gunst sammeln → Götter-Items einsetzen → Position verbessern → Fahren"; Gehrke, 2026) um klar abgegrenzte Phasen.

## Übersicht der Phasen

```
┌─ VORBEREITUNGS-PHASE ─┐   ┌─ START ─┐   ┌──── CORE: RENN-PHASE ────┐
│ Opfer-Altar           │   │ Ready   │   │ Steuern (WASD)            │
│ Chimären-Stall        │ → │  +      │ → │ Anspornen/Combat (Q/E)    │ ┐
│ Charakter-Erstellung  │   │Countdown│   │ Omen sammeln (Gunst-Zonen)│ │
│ Streitwagen-Werkstatt │   └─────────┘   │ Ziel: als Erster ins Ziel │ │
│ Strecken-/Wettkampf-  │                 └───────────────────────────┘ │
│ Auswahl               │                                               │
└───────────────────────┘                                               │
        ▲                                                                ▼
        │                  ┌─ URTEILS-PHASE ─┐   ┌─ ERHOLUNGS-PHASE (Meta) ─┐
        └──────────────────│ Gunst-Berechnung│ ← │ Wagen-Reparatur          │
                           │ Platzierung +   │   │ Strecken freischalten    │
                           │ Kopfgeld        │   │ (nach Gesamt-Gunst)      │
                           └─────────────────┘   └──────────────────────────┘
```

---

## 1) Vorbereitungs-Phase

In der Lobby/im Hub bereiten alle Spieler ihr Gespann und das Rennen vor:

- **Opfer-Altar** — Spieler opfern Publikumsgunst für Vorteile im nächsten Rennen. *Nachteile möglich* (Götter-Fluch/Kopfgeld bei Gier). → [[02_GameDesign/Götter-System#Opfer-Altar]]
- **Chimären-Stall** — Kauf & Auswahl der Zugtier-Teile (kosmetisch; das Modell bleibt gleich, Teile werden wie Skins getauscht). → [[02_GameDesign/Chimären-System]]
- **Charakter-Erstellung** — Kauf & Auswahl der Charakter-Teile (kosmetisch).
- **Streitwagen-Werkstatt** — Kauf & Auswahl des Wagens **mit Stats** (Geschwindigkeit, Gewicht). → [[02_GameDesign/Steuerung_und_Physik]]
- **Auswahl der Strecke / des Wettkampfs.** → [[02_GameDesign/Strecken_und_Umwelt]]

> [!note] Anpassung ist Taktik, nicht nur Kosmetik
> Während Chimären- und Charakter-Teile rein kosmetisch sind, bestimmen **Wagen-Stats** das Fahrgefühl (Geschwindigkeit/Gewicht) und damit das taktische Profil (GDD, Gehrke, 2026).

## 2) Start-Phase

- **Toggle Ready** durch alle Spieler.
- **Countdown** → Rennstart.

## 3) Core: Renn-Phase

- **Steuerung:** Lenken mit **WASD** (gesteuert werden die Zugtiere, nicht direkt der Wagen).
- **Anspornen / Combat:** mit **Q** und **E** brechen die Tiere ruckartig aus — um Kurven enger zu nehmen **oder** einen anderen Streitwagen anzugreifen. → [[02_GameDesign/Steuerung_und_Physik]]
- **Omen-Sammeln:** Durchfahren von **Gunst-Zonen** gibt passende Power-Ups. **Wer hinten liegt, erhält schneller Göttergunst**; Effekte sind stärker/besser je nach Göttergunst-Skala (Rubberband-/Comeback-Logik). → [[02_GameDesign/Item-Fähigkeiten]]
- **Phasen-Ziel:** als Erster das Ziel erreichen.

## 4) Erholungs-Phase (Meta-Progression)

- **Reparatur** des Wagens.
- **Freischalten neuer Strecken** basierend auf der insgesamt verdienten Publikumsgunst (Erfahrung). → [[02_GameDesign/Strecken_und_Umwelt]]

## 5) Urteils-Phase

- **Berechnung der Publikumsgunst** auf Basis von **Platzierung + Kopfgeld** (falls vorhanden). → [[02_GameDesign/Götter-System#Kopfgeld]]

---

## Spielverlauf in Renn-Phasen (Spannungsbogen)

Innerhalb der Renn-Phase entwickelt sich die Dramaturgie in vier Stufen (GDD, Gehrke, 2026):

1. **Early Game — Gedränge:** Positionierung & Massen-Management, kaum Items.
2. **Mid Game — Taktik & Sabotage:** Feld zieht sich auseinander, gezielter Item-Einsatz, erste Trümmer.
3. **Late Game — All-In / Comeback:** häufigere/extremere Götter-Events, riskante Abkürzungen; Entscheidung zwischen Sieg, Niederlage oder Totalschaden.
4. **Erholung / Meta (Lobby):** siehe oben.

> Game Flow balanciert zwischen Kontrollverlust und Meisterschaft; Frust wird durch Comeback-Potenzial (Rubberbanding) abgefedert (GDD, Gehrke, 2026).
