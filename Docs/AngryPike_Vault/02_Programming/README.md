# 💻 Programming Department

## Übersicht

Engine: **s&box** (Facepunch, Source 2-basiert) | Sprache: **C#**
Wichtig: s&box nutzt das **Component-System** — nicht das Legacy Entity-System.

---

## 📁 Inhalte

- [[Tech_Bible/README|Tech Bible]]
- [[Systems/ChariotController|ChariotController]]
- [[Systems/MultiplayerSession|Multiplayer Session]]
- [[Systems/RammingSystem|Ramming System]]
- [[Systems/GodAbilities|God Abilities]]
- [[Systems/ArenaWinConditions|Arena & Win Conditions]]

---

## 🏗️ System-Prioritäten (Build-Order)

| Priorität | System | Status |
|---|---|---|
| 1 | `ChariotController` Physics | 🔄 |
| 2 | Multiplayer Session Management | ⬜ |
| 3 | Ramming System | ⬜ |
| 4 | God Ability Architecture | ⬜ |
| 5 | Arena / Win Conditions | ⬜ |

---

## ⚙️ s&box Key-Regeln

> Diese Regeln **müssen** in allem Code beachtet werden.

- Networking-Attribute: `[Sync]`, `[Broadcast]`, `[Authority]`
- Input: `Input.AnalogMove` (kein Unity/Source-Standard)
- Immer **Component-System**, nie Legacy Entity-System

**Offizielle Doku:** https://sbox.game/dev/doc/

---

## 📁 Ordnerstruktur (im Repo)

```
_Scripts/
├── God Powers/
├── GUI/
├── Modules/
├── Networking/
├── Particles/
├── Player Chariot/
└── SFX/
```

---

## Tags

`#programming` `#sbox` `#csharp` `#tech`
