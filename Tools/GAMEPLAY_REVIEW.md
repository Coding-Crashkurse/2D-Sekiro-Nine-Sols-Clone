# Gameplay-Review – 6. September 2026

Geprüft wurden Spielersteuerung, Kampfzustände, Gegner/Bosse, Levelübergänge,
Plattformen, Checkpoints, Progression, Kamera und UI. Die Änderungen setzen am
vorhandenen Parry-Spielprinzip an. Bestehende Arbeiten an Boss, Schrein und
Abspann wurden berücksichtigt.

## Umgesetzte Verbesserungen

| Problem | Änderung |
|---|---|
| Angriff, Parade und Dash verschlucken Eingaben kurz vor Ende einer Sperre. | 120 ms Eingabepuffer; Sperren und Cooldowns gelten weiterhin. Abgelaufene Eingaben lösen keine spätere Aktion aus. |
| Ein Dash während eines Angriffs ignoriert die gewünschte Gegenrichtung. | Dash-Richtung wird direkt aus der aktuellen Bewegungseingabe bestimmt. |
| W greift die Leiter und springt im selben Frame wieder ab. | Greifen verbraucht den gemeinsamen Sprungimpuls; ein neuer Tastendruck springt ab. |
| Treffer während des Kletterns lassen die Figur an der Leiter hängen. | Ein Treffer beendet das Klettern. Der Austritt aus einer anderen überlappenden Leiter beendet die aktuelle nicht. |
| Durchfallen durch Einwegplattformen meldet weiterhin Bodenkontakt. | Temporär ignorierte Plattformen werden auch bei der Bodenprüfung ausgeschlossen; Durchfallen startet mit Abwärtsbewegung. |
| Menütasten können beim Schließen noch Spielaktionen auslösen. | Eingaben werden während der Pause und im Frame des Pausewechsels verworfen. Kontrollentzug leert Puffer und beendet Kampfaktionen. |
| Hinrichtungen und Zeitlupentimer laufen in der Pause weiter. | Hinrichtung pausiert vor Schaden und Abschluss; Hit-Stop und Zeitlupe behalten ihre Restzeit. |
| Ein Respawn kann von einer alten Gefahren-Teleportation überschrieben werden. | Respawn beendet laufende Spieler-Coroutinen und setzt Bewegungszustände zurück. |
| Hinrichtungen ignorieren den bereits berechneten Upgrade-Schaden. | Gegner verwenden den Schaden aus der AttackInfo, auch bei Hinrichtungen. |
| Alte Aschehaufen können nach erneutem Tod die neue Asche zurückgeben; Stürze hinterlassen unerreichbare Asche. | Alte Haufen werden vor dem neuen Spawn deaktiviert und entfernt. Asche fällt an der zuletzt erfassten sicheren Bodenposition. Bewegliche Plattformen ersetzen diesen Rücksetzpunkt nicht. |
| Qi-Kapazitätsupgrades sind im HUD unsichtbar. | Die Anzeige unterstützt drei bis sechs Felder und passt Beschriftung sowie Rücksetzung beim Neustart an. |
| Kurze Interaktionstastendrücke am Schrein gehen zwischen Physikschritten verloren. | Interaktion wird in Update anhand der Position abgefragt. |
| Die zentrale Parade wird erst hinter dem ersten Gegner erklärt. | Erklärung vor den ersten Kampf verschoben; am ersten Abgrund wird der Dash erklärt. |
| Screen-Shake OFF lässt Kamera-Rucke bestehen. | Die Einstellung unterdrückt auch Kicks und bereits aktive Shake-Offsets. |
| Der Autopilot meldet den Zwischenboss als Spielende und läuft an bereits geöffneten Ausgängen vorbei. | Nur der Boss in der finalen Arena beendet den Testlauf; nahe geöffnete Ausgänge werden gezielt angesteuert. |

## Validierung

- `GameplayChecks.cs`: 23 erfolgreiche Prüfungen bei simulierten 60 FPS;
  abschließender Lauf mit 27 erfolgreichen Prüfungen bei simulierten 144 FPS.
  Geprüft werden Zustandsübergänge, Pufferablauf, Menüs, Leiter, Einwegplattform,
  Hinrichtung, Asche, HUD, Schreininteraktion ohne Physikcallback und Respawn.
- Unitys bekannter Fehler in `UnityEditor.Search.SearchDatabase` wird ausdrücklich
  separat gezählt. Gameplayfehler werden weiterhin als Fehler gewertet.
- Windows-Prüf-Build: `Builds/ReviewReady/AshenSol.exe`, erfolgreich, 0 Fehler und
  0 Warnungen laut Build-Report. `-buildOutput` erlaubt ein separates Ziel,
  während eine bestehende Spielinstanz läuft.
- Protokolle: `Tools/gameplay_checks.log`, `Tools/gameplay_checks_144.log`,
  `Tools/review_build.log`, `Tools/review_final_build.log`, `Tools/review_ready_build.log`; Laufzeitprotokolle unter
  `Builds/Review/` und `Builds/ReviewFinal/`.
- Erster Lauf: Level 1 und Works einschließlich Artisan besiegt, 0 Tode,
  23 Paraden, 0 Laufzeitfehler. Der dabei entdeckte vorzeitige Autopilot-Abbruch
  wurde anschließend korrigiert.
- Separater Endbosslauf: Warden nach einem Respawn besiegt, 15 Paraden,
  117 Sekunden, 0 Laufzeitfehler.
- Nach Korrektur des Autopilots: Works → Stair nach 80 Sekunden bestätigt;
  separater Start in Stair erreicht die Bossarena nach 66 Sekunden. Damit wurden
  alle vier Gebiete in Teilstrecken geprüft. Ein einzelner erfolgreicher Durchlauf
  vom Titel bis zum Abspann ist damit nicht nachgewiesen.
  Beide Folgeläufe endeten am 180-Sekunden-Limit mit 0 Laufzeitfehlern, ohne einen
  weiteren Endboss-Sieg zu melden. Der Start in Stair hatte dabei einen Tod;
  der Start in Works blieb ohne Tod, hatte aber mehrere Stürze in Stair.

Wiederholung der Spieltests mit Unity im Batchmodus:

```text
-batchmode -nographics -disable-assembly-updater
-projectPath C:/Users/User/Desktop/nine_sols/AshenSol
-executeMethod AshenSol.EditorTools.GameplayChecks.Run
-checkFps 144 -mute -logFile C:/Users/User/Desktop/nine_sols/Tools/gameplay_checks_144.log
```

Kein `-quit` angeben: Der Runner beendet Unity nach den Tests. Bei dem in README
beschriebenen ShaderGraph-GUID-Fehler zuvor `Tools/fix_packages.sh` ausführen.

## Verbleibende Designfragen

- Viele Hinweise und Menüs nennen weiterhin Tastaturtasten, auch mit Gamepad.
  Eine zentrale Anzeige passend zum zuletzt verwendeten Eingabegerät wäre sinnvoll.
- Hinrichtungen töten einfache Gegner schon ohne Schadensupgrade. Wie stark sich
  EDGE gegenüber VIGOR anfühlt, sollte ein menschlicher Durchlauf bewerten.
- Trefferabfragen erzeugen im Kampf mit `OverlapBoxAll` temporäre Arrays.
  Ein Profilerlauf auf der Zielhardware sollte klären, ob das messbare Spitzen
  verursacht, bevor diese Stellen umgebaut werden.
- Automatische Zustandsprüfungen beurteilen weder subjektive Schwierigkeit noch
  Bildlesbarkeit. Versteckte Windows-Testläufe lieferten schwarze Screenshots;
  daraus wird keine visuelle Freigabe abgeleitet.
