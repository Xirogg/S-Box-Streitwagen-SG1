---
titel: KI-Workflow & Protokoll
tags: [producing, ki, methodik, transparenz]
status: reviewed
quelle: Research Bible Producing & QM-Strategie (Würfl, 2026)
aktualisiert: 2026-05-30
---

# KI-Workflow & Protokoll

Das Team dokumentiert die Nutzung von KI-Tools transparent. Grundsatz: **Alle Quellen werden vom Verfasser selbst gelesen und auf Korrektheit geprüft** (QM-Strategie Research Bible, Würfl, 2026).

## Methodik (Cross-Prompting)

(1) **Opus** erstellt Initial-Struktur und Recherche-Plan → (2) **Sonnet** führt iterative Anpassungen aus (Quellen-Updates, Tool-Stack-Korrektur, Department-Korrektur) → (3) **Opus** konsolidiert. Quellen-Speisung über das Anthropic-Projects-Feature, manuelle Quellenprüfung durch den Verfasser.

Eingesetzte Modelle: Claude Opus 4.7 + Claude Sonnet 4.6 (Anthropic) im Wechsel (Research Bible Producing, Würfl, 2026).

## Beispiel-Iterationsschritte (Marketing-Teil)

| Schritt | Prompt-Zweck | Verifikation |
|---|---|---|
| 1 | Briefing & 12-Kapitel-Struktur, Premium-Pivot | Struktur durch Verfasser definiert (Opus) |
| 2 | Friendslop-Vergleichsdaten extrahieren | Querprüfung mit PDF durch Verfasser |
| 3 | Web-Recherche Marketing (Zukowski, GDC) | URL-Check je Quelle (Sonnet) |
| 4 | Pivot-Begründung F2P → Premium | inhaltliche Endentscheidung durch Verfasser |
| 5 | Stakeholder-Workflow, RACI-Verknüpfung | Strukturvorgabe durch Verfasser |

## Prompt-Engineering-Grundlage (akademisch)

Die Methodik stützt sich auf systematische Übersichten zum Prompt Engineering in LLMs (Sahoo et al., 2024; Schulhoff et al., 2024; Vatsal & Singh, 2024).

> [!info] Hinweis für diesen Vault
> Auch dieser Vault folgt dem Prinzip: projekt-interne Fakten sind an Teamdokumente gebunden, externe Aussagen sind nach APA belegt ([[07_Quellen/Quellenverzeichnis_APA]]), und es werden nur frei zugängliche Quellen verwendet.
