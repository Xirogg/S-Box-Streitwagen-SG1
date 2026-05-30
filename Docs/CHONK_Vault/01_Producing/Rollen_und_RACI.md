---
titel: Rollen & RACI-Matrix
tags: [producing, organisation, raci]
status: reviewed
quelle: Research Bible Producing (Würfl, 2026)
aktualisiert: 2026-05-30
---

# Rollen & RACI-Matrix

## Departments & Verantwortung

| Department | Kernaufgaben | Letzte Entscheidung über… |
|---|---|---|
| **Game Design** | Mechaniken, Level Design, Balancing, GDD-Pflege | Spielregeln, Core Loop |
| **Tech Art** | Shader, VFX, technische Asset-Pipeline, Style Guide | visuell-technische Umsetzung |
| **2D Art*** | Concept Art, 2D-Sprites, UI-Grafiken, Texturen | 2D-visueller Stil |
| **Programming** | Engine-Code, Tools, Build-System, Repo-Pflege | Architektur, Tech-Stack |
| **Producing** | Planung, Scope, Kommunikation, Pitches | Scope, Deadlines, Priorität |

\* **Sondersituation:** Der 2D-Artist hat das Team verlassen. Das Department bleibt bestehen, die Aufgaben werden vom Restteam gemeinsam getragen; der Scope wurde bewusst auf kugeliges Friendslop-Design reduziert (Research Bible Producing, Würfl, 2026).

Disziplinen-Bandbreite angelehnt an Tonogame (2024), Beamable (2022), NYFA (o. J.).

## RACI-Matrix für zentrale Prozesse

> RACI = **R**esponsible (führt aus) · **A**ccountable (verantwortet, genau eine Person) · **C**onsulted (wird konsultiert) · **I**nformed (wird informiert). Die Methode löst unklare Zuständigkeiten in cross-funktionalen Teams (Gamestorming, o. J.).

| Prozess / Artefakt | GD | TArt | 2DArt | Prog | Prod |
|---|---|---|---|---|---|
| GDD-Pflege | A/R | C | C | C | I |
| 2D-Asset-Erstellung* | C | C | A/R | I | I |
| Tech-Art-Integration (Shader, VFX) | C | A/R | C | C | I |
| Feature-Implementierung | C | C | I | A/R | I |
| Sprint-Planung | C | C | C | C | A/R |
| Stakeholder-Pitches | C | I | I | I | A/R |
| Bug Triage | C | I | I | R | A |
| Scope-Entscheidungen | C | C | C | C | A/R |

Golden Rule: pro Aufgabe genau **eine** accountable Person (Gamestorming, o. J.).

## Übergabepunkte (Handoffs)

Mangelhafte Kommunikation an Disziplin-Schnittstellen zählt zu den häufigsten Problemfaktoren der Spielentwicklung (Petrillo et al., 2009). Definierte Handoff-Artefakte reduzieren Missverständnisse (Schwaber & Sutherland, 2020):

`Game Design → Feature-Spec + Reference` → `Tech Art → Asset im Styleguide-Format` → `Programming → Build mit Test-Notes` → `Producing (QA)`

Parallel (2D Art): `2D Art (Sprite/UI) → Tech Art (Integration) → Producing (QA)`

## Verknüpft
- [[01_Producing/Tool-Stack]] · [[01_Producing/QM_Definition_of_Done]] · [[01_Producing/Produktionsphasen_StageGates]]
