---
titel: Vault-Anleitung
tags: [start, anleitung, obsidian]
status: living
aktualisiert: 2026-05-30
---

# 🚀 Vault-Anleitung — Setup & Nutzung

Für alle Teammitglieder. Einmal einrichten, danach läuft die Synchronisation automatisch.

---

## Schritt 1 — Obsidian installieren

Obsidian von der offiziellen Seite herunterladen und installieren: **https://obsidian.md**

Obsidian ist eine lokal-first Wissensmanagement-App auf Basis einfacher Markdown-Dateien (`.md`); es besteht keine Cloud-Abhängigkeit, alle Dateien liegen lokal und lassen sich per Git teilen (Obsidian, o. J.).

> [!warning] Linux
> Auf Linux **nicht** über Flatpak oder Snap installieren — das verträgt sich schlecht mit dem Git-Plugin (Vinzent03, o. J.).

---

## Schritt 2 — Vault öffnen

1. Obsidian starten → **„Open folder as vault"**
2. Den Ordner `CHONK_Vault/` aus dem geklonten GitHub-Repo auswählen
3. Obsidian öffnet die Vault mit allen Notizen. Startseite ist [[00_Start/🏠 Home|🏠 Home]].

**Empfehlung:** Den Vault innerhalb des bestehenden GitHub-Repos ablegen (z. B. unter `docs/CHONK_Vault/`), damit alle Notizen automatisch mitversioniert werden — passend zum bestehenden GitHub-Workflow des Programming-Departments (Tech Bible, Lange, 2026).

---

## Schritt 3 — Team-Sync über das Git-Plugin

Da das Team bereits GitHub nutzt, übernimmt das **Obsidian-Git-Plugin** die Synchronisation (Vinzent03, o. J.):

1. **Settings → Community Plugins** → „Turn on community plugins"
2. **Browse** → „Git" suchen → **Install** → **Enable**
3. In den Plugin-Einstellungen unter *Authentication/Commit Author* Name/E-Mail eintragen
4. Intervalle setzen:
   - `Auto pull on startup`: an
   - `Auto commit-and-sync interval`: z. B. **10** Minuten
   - `Commit message`: `vault: auto-sync {{date}}`

> [!info] Git ist kein Live-Sync
> Das Plugin pullt/pusht in Intervallen — es ist **kein** Live-Co-Editing wie Google Docs, eignet sich aber ideal für asynchrone Zusammenarbeit (Vinzent03, o. J.). Nicht zwei Personen gleichzeitig dieselbe Notiz bearbeiten.

> [!tip] Mobile
> Auf Android/iOS läuft das Plugin über isomorphic-git (nur HTTPS, kein SSH; Authentifizierung per Personal Access Token). Bei großen Vaults kann das langsam sein (Vinzent03, o. J.).

---

## Schritt 4 — Arbeiten im Vault

| Aktion | So geht's |
|---|---|
| Interner Link | `[[` tippen → Notiz auswählen (z. B. `[[Game-Loop]]`) |
| Tag setzen | `#draft`, `#reviewed`, `#final` in den Text oder ins Frontmatter |
| Volltextsuche | `Ctrl/Cmd + Shift + F` |
| Graph-Ansicht | linke Sidebar → Graph-Icon (zeigt die Vernetzung der Notizen) |
| Neue Notiz aus Template | Notiz aus `99_Templates/` kopieren |

---

## Konventionen in diesem Vault

- **Dateinamen:** `Thema_Unterthema.md`, sprechend, keine Sonderzeichen außer `_` und `-`.
- **Frontmatter:** jede Notiz hat oben einen YAML-Block (`tags`, `status`, `aktualisiert`).
- **Status-Tags:** `#draft` (Entwurf) · `#reviewed` (geprüft) · `#final` (final).
- **Quellen:** externe Aussagen mit `(Autor, Jahr)` belegen und im [[07_Quellen/Quellenverzeichnis_APA|Quellenverzeichnis]] führen.
- **Meetings:** `06_Meetings/JJJJ-MM-TT_Thema.md` aus [[06_Meetings/_Template_Meeting|Meeting-Template]].

---

## `.gitignore`-Empfehlung

```gitignore
# Lokale Obsidian-Workspace-Dateien (nicht teilen)
.obsidian/workspace.json
.obsidian/workspace-mobile.json
.obsidian/cache

# Betriebssystem
.DS_Store
Thumbs.db

# Die übrige .obsidian/-Konfiguration (Plugins, app.json) wird geteilt,
# damit alle dasselbe Setup haben → NICHT ignorieren.
```

---

## Quellen (APA)

- Obsidian. (o. J.). *Obsidian help documentation.* https://help.obsidian.md (Abgerufen am 30. Mai 2026)
- Vinzent03. (o. J.). *Obsidian Git* [Community-Plugin & Dokumentation]. GitHub. https://github.com/Vinzent03/obsidian-git (Abgerufen am 30. Mai 2026)

Vollständige Liste: [[07_Quellen/Quellenverzeichnis_APA]].
