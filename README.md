# ASHEN SOL

Ein kleines, parry-lastiges 2D-Action-Spiel im Stil von *Nine Sols* (Taopunk: Taoisten-Tempelruinen
trifft Cyberpunk). Zwei Level: ein Korridor-Level mit Gegnern und Plattforming, dahinter ein
versiegeltes Tor zur Bossarena.

## Spielen

Fertiger Build: **`Builds/Windows/AshenSol.exe`** — einfach doppelklicken.

### Steuerung

| Aktion | Tastatur | Gamepad |
|---|---|---|
| Bewegen | A / D | Linker Stick |
| Springen | Leertaste / W | A (Süd) |
| Angreifen | J / Linksklick | X (West) |
| **Parieren** | K / Rechtsklick | B (Ost) |
| Dash | L / Shift | RB / RT |
| Qi-Blast | I | Y (Nord) |
| Heilen | H | LB |
| Pause | Esc | Start |

### Die Kampfregel

* **Weißes Aufblitzen** = parierbarer Angriff → im richtigen Moment **K** drücken.
  Perfekte Parade: kein Schaden, +1 Qi, und viel **Haltungsschaden** auf der gelben Leiste.
  Etwas zu spät: Block mit 30 % Restschaden (füllt die gelbe Leiste nur ein Viertel so schnell).
* **Rotes Aufblitzen** = unblockbar → Parieren hilft nicht, **wegdashen oder springen**.
* Jeder Gegner hat **zwei Leisten**: rot = Leben, gelb = **Haltung**.
  Ist die gelbe Leiste voll, **bricht die Deckung**: der Gegner ist **3 Sekunden wehrlos**,
  nimmt doppelten Schaden, und über ihm erscheint ein pulsierendes **I**.
* **Hinrichtung: `I`** neben einem gebrochenen Gegner → kostet 1 Qi, richtet massiven Schaden an
  (Grunt 60, Sentinel 85, Drohne 45, Boss 130). Danach ist die Haltungsleiste wieder leer.
* **Normale Gegner brechen bei einer einzigen perfekten Parade.** Der **Boss braucht drei** —
  seine Dreierschlag-Kombo ist genau dafür gemacht.
* Die Haltung regeneriert nach 1,6 s Ruhe wieder. Wer nicht nachsetzt, verliert den Fortschritt.
* Ohne Ziel in Reichweite bleibt `I` der **Qi Blast**: 12 Schaden im Umkreis plus kräftiger
  Haltungsschaden — gut, um eine Gruppe gleichzeitig aufzubrechen.
* Gegnerprojektile lassen sich mit einer perfekten Parade **zurückschleudern** (30 Schaden).

**Qi** (max. 3, goldene Sechsecke): +1 pro perfekter Parade und pro Kill. Ausgeben kannst du es für
die **Hinrichtung (I)**, den **Qi Blast (I ohne Ziel)** oder **Heilen (H)**.

### Schwierigkeitsgrade

Im Titelmenü umschaltbar (wird gespeichert):

| | DISCIPLE | SOL SLAYER |
|---|---|---|
| Perfektes Parry-Fenster | 0,22 s | 0,14 s |
| Erlittener Schaden | ×0,8 | ×1,3 |
| Gegner-Telegraphen | ×1,12 (länger) | ×0,9 (schneller) |
| Heilung | 40 HP | 30 HP |

Außerdem im Menü: Musik- und Soundlautstärke, Screen-Shake an/aus.

## Projektaufbau

```
nine_sols/
├─ AshenSol/            Unity-Projekt (Unity 6000.4.6f1, URP 2D, Input System, uGUI)
│  └─ Assets/_Game/
│     ├─ Scripts/       Core, Player, Enemies, Boss, Level, UI, VFX, Audio
│     ├─ Shaders/       Silhouette + SpriteAdditive (URP)
│     ├─ Resources/     Sprites, Audio/SFX, Audio/Music, Fonts
│     ├─ Scenes/Main.unity   die einzige Szene: ein GameBootstrap-Objekt
│     └─ Editor/        Bootstrap, Build-Skript, Import-Postprozessoren
├─ Tools/               SPEC.md + alle Skripte (siehe unten)
├─ AudioRaw/            unbearbeitete ElevenLabs-Ausgaben + sfx_map.txt
├─ Screenshots/         Autopilot-Aufnahmen
└─ Builds/Windows/      der gebaute Player
```

**Alles wird zur Laufzeit aus Code gebaut** — keine Prefabs, keine handgebaute Szene. Level,
Spieler, Gegner, Boss, UI und Effekte entstehen in `LevelBuilder`, `PlayerController.Create` usw.
Die Figuren sind prozedurale Puppen (Gelenk-Hierarchien aus Einzelsprites) mit Verlet-Stoff für
Schärpe und Umhang.

## Werkzeuge

| Skript | Zweck |
|---|---|
| `Tools/compile.sh` | Kompilieren + Projekt-Bootstrap (Layer, Shader, Player-Settings, Szene) |
| `Tools/build.sh` | Windows-Player nach `Builds/Windows/AshenSol.exe` bauen |
| `Tools/autopilot.sh [sek]` | Build mit Bot durchspielen lassen, Screenshots + Log nach `Screenshots/` |
| `Tools/gen_sprites.py` | alle 71 Sprites neu generieren (Pillow) + `Tools/contact_sheet.png` |
| `Tools/import_audio.py` | ElevenLabs-Rohdateien trimmen, normalisieren und ins Projekt kopieren |
| `Tools/fix_packages.sh` | umgeht einen Unity-6000.4.6f1-Bug im ShaderGraph-Paket (läuft automatisch) |

Der Autopilot (`-autopilot`) ersetzt die Eingabe durch einen Bot, der pariert, dashed, das Level
durchquert und den Boss besiegt. Am Ende schreibt er eine Zeile
`AUTOPILOT RESULT: reachedGate=… bossDefeated=… exceptions=…` — der schnellste Regressionstest.

## Bekannte Eigenheiten der Umgebung

* Unity 6000.4.6f1 schreibt beim ersten fehlgeschlagenen Compile Dateien im ShaderGraph-Paket
  kaputt. `Tools/fix_packages.sh` stellt sie her und ergänzt das fehlende `using` — läuft vor jedem
  Build automatisch.
* Die Paketextraktion schlägt gelegentlich mit `EPERM` fehl; die Skripte wiederholen es automatisch.
* `GUI/Text Shader` darf **nie** in „Always Included Shaders" — das bricht den Player-Build.
