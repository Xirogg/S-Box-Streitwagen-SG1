# 🚀 AngryPike Obsidian — Setup-Anleitung

> Für alle Teammitglieder. Einmalig durchführen, dann läuft alles automatisch.

---

## Schritt 1 — Obsidian installieren

1. https://obsidian.md → „Get Obsidian for free" → herunterladen & installieren

---

## Schritt 2 — Vault öffnen

1. Obsidian starten → **„Open folder as vault"**
2. Den `AngryPike_Vault/`-Ordner aus dem geklonten GitHub-Repo auswählen
3. Obsidian öffnet die Vault mit allen Dokumenten

---

## Schritt 3 — Obsidian Git Plugin einrichten

*(Für automatischen Team-Sync über GitHub)*

1. **Settings** (Zahnrad) → **Community Plugins** → „Turn on community plugins"
2. **Browse** → „Obsidian Git" suchen → **Install** → **Enable**
3. Plugin-Einstellungen öffnen:
   - `Auto pull interval`: **10** (Minuten)
   - `Auto push interval`: **10** (Minuten)
   - `Commit message`: `vault: auto-sync {{date}}`
4. Fertig — Obsidian Git synchronisiert automatisch alle 10 Minuten

**Quelle:** Vinzent Schneider (o.J.). *Obsidian Git*. GitHub. https://github.com/denolehov/obsidian-git

---

## Schritt 4 — Startseite aufrufen

- Linke Sidebar → `00_Overview/README.md` öffnen
- Oder: `Ctrl+O` → „README" eingeben

---

## Wichtige Shortcuts

| Shortcut | Funktion |
|---|---|
| `Ctrl+O` | Datei öffnen / suchen |
| `Ctrl+N` | Neue Notiz |
| `Ctrl+Shift+F` | Volltext-Suche |
| `Ctrl+G` | Graph View öffnen |
| `[[` tippen | Internen Link erstellen |
| `#` tippen | Tag erstellen |

---

## Neue Meeting-Protokolle anlegen

1. `05_Meetings/_Template_Meeting.md` öffnen
2. `Ctrl+P` → „Copy file" → Datum im Namen setzen: `2026-05-30_Weekly.md`
3. Felder ausfüllen
4. In `05_Meetings/README.md` verlinken

---

## Quellen (APA)

- Obsidian. (o.J.). *Obsidian Help Documentation*. https://help.obsidian.md (Abgerufen am 28. Mai 2026)
- Vinzent Schneider. (o.J.). *Obsidian Git*. GitHub. https://github.com/denolehov/obsidian-git (Abgerufen am 28. Mai 2026)
