---
titel: Code-Konventionen
tags: [programming, codestyle, konventionen]
status: reviewed
quelle: Tech Bible (Lange, 2026)
aktualisiert: 2026-05-30
---

# Code-Konventionen

## Struktur jedes Skripts

Die ersten drei Funktionen (falls vorhanden) immer in dieser Reihenfolge (s&box-Komponenten-Lebenszyklus):

1. `OnAwake()`
2. `OnStart()`
3. `OnUpdate()`

- **Keine** großen Codeblöcke in diesen Funktionen — vor allem nicht in `OnUpdate()`.
- Andere Funktionen so klein wie möglich; wo möglich unterteilen.
- Zusammengehörige geteilte Funktionen untereinander platzieren.
- **Regionen** für große zusammengehörige Blöcke nutzen und sinnvoll benennen.
- Funktionen mit Kommentar dokumentieren, wenn nicht trivial. Kommentare **über** Block/Funktion.
- Debug-Logs klar benennen.

## Sichtbarkeit (so eng wie möglich)

- Variablen nur `public`, wenn nötig (z. B. Zugriff mehrerer Funktionen) — sonst am Funktionsanfang deklarieren.
- Variablen nur `protected`, wenn nötig — sonst `private` lassen.
- Funktionen nur `public`, wenn nötig (z. B. Zugriff anderer Skripte/Events).
- Editor-Variablen sauber in Kategorien einteilen; Variablendeklaration bevorzugt in `OnAwake()`.

## Benennung & Lesbarkeit

| Element | Schema | Beispiel |
|---|---|---|
| Variablen | `lowerCamelCase` | `currentSpeed` |
| Booleans | Präfix `is` / `has` / `can` | `isBoosting` |
| Konstanten | `UPPER_SNAKE_CASE` | `MAX_SPEED` |
| Funktionen / Klassen / GameObjects | `PascalCase` | `ChariotController` |

Immer logische, lesbare Namen (Tech Bible, Lange, 2026).

## Verknüpft
- Engine-Attribute ([Sync]/[Broadcast]/[Authority]) → [[03_Programming/Tech-Stack_und_Engine]]
- Architektur → [[03_Programming/Systemarchitektur]]
