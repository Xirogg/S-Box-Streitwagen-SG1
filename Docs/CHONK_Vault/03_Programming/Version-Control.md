---
titel: Version Control (GitHub)
tags: [programming, git, github, workflow]
status: reviewed
quelle: Tech Bible (Lange, 2026)
aktualisiert: 2026-05-30
---

# Version Control (GitHub)

GitHub ist die Software für Versionskontrolle; der Repo-Link liegt im Team-Discord und als Doc-Karte in Codecks (Tech Bible, Lange, 2026).

## Guidelines

- Der **Main-Branch** trägt den aktuellen Build.
- Es wird **nichts direkt auf Main** gepusht.
- Branches sorgfältig benennen, Schema: `Department - featureBranch` (Bsp. `TechArt - insertFeatureBranch`).
- Commit-Zusammenfassungen dürfen mit CoPilot verfasst werden.

## Bezug zum Obsidian-Vault

Der Vault lebt im selben GitHub-Repo (z. B. `docs/CHONK_Vault/`) und wird über das Obsidian-Git-Plugin synchronisiert (Vinzent03, o. J.). So sind alle Notizen mitversioniert. → Setup: [[00_Start/Vault-Anleitung]]

> [!tip] Branch-Disziplin auch für Docs
> Größere Doku-Umbauten ebenfalls über Feature-Branches, damit Main stabil bleibt.

## Verknüpft
- Tool-Stack → [[01_Producing/Tool-Stack]]
- Engine/Tech → [[03_Programming/Tech-Stack_und_Engine]]
