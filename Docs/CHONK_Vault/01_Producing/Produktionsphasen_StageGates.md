---
titel: Produktionsphasen & Stage Gates
tags: [producing, prozess, stagegate]
status: reviewed
quelle: Research Bible Producing (Würfl, 2026)
aktualisiert: 2026-05-30
---

# Produktionsphasen & Stage Gates

## Phasenmodell

Game Development Software Engineering (GDSE) lässt sich in drei Hauptphasen fassen — Pre-Production, Production, Post-Production —, wobei Alpha (Feature Complete) und Beta (Content Lock) als Sub-Phasen der Production gelten (Aleem, Capretz & Ahmed, 2016).

```
PRE-PRODUCTION → PRODUCTION → ALPHA → BETA → RELEASE
(Konzept, GDD,   (Asset-Build, (Feature  (Content (Gold Master,
 Prototyp)        Feature-Dev)  Complete) Lock)    Launch)
```

Industrie-Meilensteine: First Playable → Alpha → Beta → Gold Master (Aleem et al., 2016).

## Pre-Production (aktuelle Phase)

Pre-Production ist die Phase der Machbarkeitsprüfung inkl. Anforderungserhebung und Marketingstrategie; im Indie-Kontext entstehen hier GDD, erste Prototypen und ein Vertical Slice (Aleem et al., 2016; KinematicSoup, 2016).

| Aktivität | Output / Deliverable | Hauptverantwortung |
|---|---|---|
| Konzept & Vision | Vision Statement, Design Pillars, GDD | Game Design |
| Marktanalyse & Zielgruppe | Wettbewerbsanalyse, Personas | Producing |
| Prototyping (Core Loop) | spielbarer Prototyp | Programming + Game Design |
| Asset-Style-Test | Style-Guide, Mood Boards | Tech Art (+ 2D Art) |
| Tech-Setup | Git-Repository | Programming |

## Methodik

- **Lightweight Scrum** mit 1-Wochen-Sprints, Daily-Sync via Discord, Sprint-Retro dienstags (Research Bible Producing, Würfl, 2026; Schwaber & Sutherland, 2020).
- Producing hat den **Final-Call** bei Scope und Deadlines.
- Häufigste Probleme der Spielentwicklung sind nicht technisch, sondern kommunikativ/prozessual (Petrillo et al., 2009) → klare Handoffs ([[01_Producing/Rollen_und_RACI]]).

## Stage-Gate-Status

| Stage Gate | Inhalt | Status |
|---|---|---|
| Stage Gate 1 | Konzept, GDD, Tech Bible, TAD, Research Bibles, erster Prototyp | ✅ abgegeben |
| Stage Gate 2 | — | 🔄 in Arbeit |

Beim **Stage-Gate-Playtest** wird ein vollständiges 5-Minuten-Rennen gegen alle fünf Vision-Anker bewertet (Go/No-Go) — siehe [[01_Producing/QM_Definition_of_Done]].
