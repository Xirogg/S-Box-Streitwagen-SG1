---
titel: Steuerung & Physik
tags: [gamedesign, steuerung, physik, combat]
status: reviewed
quelle: GDD (Gehrke, 2026)
aktualisiert: 2026-05-30
---

# Steuerung & Physik

## Grundsteuerung

- **WASD** steuert die **Zugtiere** — der Wagen wird **nicht direkt** gesteuert, sondern physik-basiert hinterhergezogen.
- Voraussetzung für Spezialmanöver: eine gewisse Grundgeschwindigkeit (Vorwärts über **W**), damit Q/E physikalisch korrekt greifen.
- **B** löst eine erhaltene Item-Fähigkeit aus. → [[02_GameDesign/Item-Fähigkeiten]]

## Kamera

Dynamische 3rd-Person-Follow-Kamera, starr hinter dem Wagen. Bei Höchstgeschwindigkeit weitet sich das **FOV** und ein leichter **Screen-Shake** setzt ein (Geschwindigkeitsgefühl).

## Ruck-Manöver / Combat (Q & E)

**Q/E** lassen die Zugtiere ruckartig nach links/rechts ausbrechen — **Angriffsmechanik und Fahrzeugkontrolle zugleich**.

- **Tap statt Hold:** Aktion wird nur durch Antippen ausgelöst; Halten = wie einmaliges Antippen, keine Daueraktion.
- Ablauf (Beispiel Taste **E** / Rechtsruck):
  1. **Ausbruch** — Tiere rucken nach rechts (können dabei Gegner durch Ramm-Schaden verletzen).
  2. **Nachschwingen (Overshoot)** — Wagen wird nachgezogen, übersteuert leicht (der ausschwingende Wagen wirkt als Waffe).
  3. **Normalisierung** — Gespann fängt sich, Fahrt normalisiert sich.
  - (**Q** = derselbe Ablauf spiegelverkehrt nach links.)

### Spamming & Cooldown
Schnell wiederholtes Q/E lässt die Tiere **kontinuierlich** in eine Richtung ziehen (ohne Normalisierung dazwischen) → Fliehkräfte bekämpfen, Kurven sehr eng nehmen. Ein **minimaler Cooldown** zwischen registrierten Rucks verhindert „Teleportieren" des Wagens.

## Fahrgefühl nach Stats

Jeder Wagen besitzt sechs Stats — **Tempo, Handling, Drift, Gewicht, Angriff, Robustheit** — die auf Engine-Backend-Variablen gemappt sind (z. B. Tempo → `Pullforce`, Robustheit → `MaxHP`). Vollständige Tabelle, Presets und Combat-Referenz: [[02_GameDesign/Wagen-Stats_Balancing]].

Das Zusammenspiel von **Geschwindigkeit × Gewicht** prägt den Charakter:

| Profil | Fahrgefühl | Vorteil | Nachteil |
|---|---|---|---|
| schnell + leicht | nervös, instabil, driftet | maximale Agilität | geringe Haftung; eigener Rückstoß schleudert weit weg |
| schnell + schwer | „Geschoss", hohes Momentum | räumt Gegner weg | träge aus Kurven, kaum schnelle Ausweichmanöver |
| langsam + leicht | extrem direkt, „substanzlos" | höchste Präzision, engste Innenbahn | wird von Remplern schwerer Gegner weggeworfen |

(GDD, Gehrke, 2026)

## Physik-Formeln

| Größe | Formel | Bedeutung |
|---|---|---|
| **Fliehkraft** | `F = m · (v² / r)` | m = Masse/Gewicht, v = Geschwindigkeit, r = Kurvenradius |
| **Beschleunigung** | `a = 100 / Gewicht` (pro s) | Zugtierkraft konstant (Bsp. 100); schwerer = träger. Bsp.: 100/10 = 10 vs. 100/50 = 2 |
| **Wucht (Rammen)** | `Wucht = Gewicht · aktuelle Geschwindigkeit` | wer weniger Wucht hat, wird weggestoßen; Schaden richtet sich nach dem **ATK-Stat** des Angreifers |

> [!tip] Programmierhinweis (s&box)
> Diese Werte gehören in den `ChariotController` (höchste Build-Priorität, siehe [[03_Programming/Systemarchitektur]]). Tap-Logik mit Cooldown serverautoritativ umsetzen, Netzwerk-Sync über die s&box-Component-Attribute. → [[03_Programming/Tech-Stack_und_Engine]]
