# AutoKey

Kostenloses Windows-Programm, das automatisch Tasten und Maustasten für dich drückt –
einzeln oder als Ablauf, in festen Abständen, ab einer bestimmten Uhrzeit oder dauerhaft gehalten.

- **Eine einzige Datei**: `AutoKey.exe` (ca. 130 KB), keine Installation nötig
- Läuft unter **Windows 10 und 11** (nutzt das dort eingebaute .NET Framework 4.8)
- Deutsche Oberfläche, Einstellungen werden automatisch gespeichert

## Download & Start

1. [`release/AutoKey.exe`](release/AutoKey.exe) öffnen und rechts oben auf **Download** (Pfeil-Symbol) klicken.
2. Die Datei irgendwo ablegen (z. B. Desktop) und per Doppelklick starten.

> **Windows-Warnung beim ersten Start?** Da das Programm nicht kostenpflichtig digital signiert ist,
> zeigt Windows SmartScreen evtl. „Der Computer wurde durch Windows geschützt“.
> Dann auf **Weitere Informationen → Trotzdem ausführen** klicken.

## Funktionen

### Ablauf (mehrere Schritte)

Statt einer einzelnen Taste kannst du einen **Ablauf** zusammenstellen – ideal für monotone Aufgaben in Spielen:

```
1.  1             (50 ms)   →  800 ms warten
2.  Maus links    (50 ms)   →  2 s warten
3.  E             (50 ms)
```

- **+ Hinzufügen / – Entfernen / ▲ ▼** in der Liste, darunter wird der ausgewählte Schritt bearbeitet.
- **Taste / Maustaste:** alle üblichen Tasten, außerdem **Maus links/rechts/Mitte** und die **Seitentasten** –
  oder **Aufnehmen …** klicken und die Taste (auch Strg+S usw.) drücken.
- **Haltedauer:** wie lange gedrückt wird (bis 10 Minuten – z. B. zum „Gedrückthalten“ beim Abbauen).
- **Danach warten:** Pause bis zum nächsten Schritt.

### Wann?

| Einstellung | Bedeutung |
|---|---|
| **Modus** | *Ablauf wiederholt ausführen* – oder *Tasten dauerhaft gedrückt halten* (z. B. Auto-Laufen mit W, bis du stoppst) |
| **Wiederholen alle** | Abstand zwischen zwei Durchläufen (ms bis Stunden) |
| **Zufällig variieren** | Abstand um ± x % verändern |
| **Wiederholungen** | Nach so vielen Durchläufen stoppen – `0` = unbegrenzt |
| **Beginnen** | *nach X Sekunden* oder *um HH:MM:SS Uhr* |
| **Automatisch stoppen** | Optional zu einer bestimmten Uhrzeit beenden |

### Spiel / Zielfenster

Unter **Nur senden an** das Spiel auswählen. Dann gilt:

- Tasten gehen **nur an dieses Spiel** – wechselst du per Alt+Tab z. B. in den Browser oder Discord,
  **pausiert AutoKey automatisch** (gedrückte Tasten werden losgelassen) und macht weiter, sobald du zurück im Spiel bist.
- Wird das Spiel beendet, **stoppt AutoKey** von selbst.
- **Scancode senden** einschalten, wenn ein Spiel die Tasten sonst nicht annimmt.

### Profile

Oben **Speichern unter …** sichert Ablauf, Zeitplan und Zielfenster unter einem Namen (z. B. „Angeln“, „Farmen“).
Über die Auswahlliste lädst du ein Profil mit einem Klick wieder.

### Im Hintergrund laufen lassen

- **F6** (änderbar) startet und stoppt – auch mitten im Spiel, ohne das Fenster zu öffnen.
- **Nach dem Start unsichtbar im Hintergrund weiterlaufen:** das Fenster verschwindet beim Start;
  am Symbol neben der Uhr zeigt ein **grüner Punkt**, dass AutoKey läuft (Rechtsklick: Start/Stopp, Fenster, Beenden).
- **Benachrichtigungen** (z. B. „Fertig“) lassen sich abschalten.
- **Standby verhindern**, solange AutoKey läuft.

**Wichtig:** Ohne gewähltes Zielfenster geht der Tastendruck immer an das **gerade aktive Fenster** –
die Statuszeile zeigt einen Countdown bis zum Start.

### Beispiele

- **Alle 30 Sekunden Leertaste, endlos:** ein Schritt `Leertaste`, Wiederholen alle `30 Sekunden`, Wiederholungen `0`.
- **Angeln in einem Spiel:** Schritt 1 `Maus rechts` → `1500 ms` warten, Schritt 2 `Maus rechts`; Wiederholen alle `20 Sekunden`;
  Nur senden an: das Spiel.
- **Auto-Laufen:** Schritt `W`, Modus *dauerhaft gedrückt halten*.
- **Einmal um 14:30 Uhr Enter drücken:** Schritt `Enter`, Wiederholungen `1`, Beginnen `um 14:30:00`.

### Startoptionen (für Verknüpfungen oder die Windows-Aufgabenplanung)

```
AutoKey.exe /start    sofort mit den zuletzt gespeicherten Einstellungen loslegen
AutoKey.exe /tray     unsichtbar im Infobereich starten
```

Einstellungen und Profile liegen in `%APPDATA%\AutoKey\`.

## Hinweise

- Läuft das Zielprogramm **als Administrator**, muss auch AutoKey als Administrator gestartet werden
  (Rechtsklick → *Als Administrator ausführen*), sonst blockiert Windows die Eingaben. AutoKey zeigt das dann an.
- Ein **gesperrter PC** (Win+L) nimmt keine Tastendrücke an.
- **Online-Spiele:** Viele Spiele verbieten Makros/Automatisierung in ihren Nutzungsbedingungen, und Anti-Cheat-Systeme
  können simulierte Eingaben erkennen. AutoKey versucht bewusst **nicht**, sich davor zu verstecken – bitte prüfe die Regeln
  deines Spiels, bevor du AutoKey dort nutzt, um eine Sperre deines Kontos zu vermeiden.

## Selbst bauen

Quellcode: C# / Windows Forms in [`src/AutoKey`](src/AutoKey).

- **Visual Studio 2022:** `AutoKey.sln` öffnen → *Erstellen*.
- **Kommandozeile** (.NET SDK 8 oder neuer, funktioniert auch unter Linux/macOS):

  ```
  dotnet build src/AutoKey/AutoKey.csproj -c Release
  ```

  Ergebnis: `src/AutoKey/bin/Release/net48/AutoKey.exe`

Bei jedem Push baut GitHub Actions die `.exe` automatisch (Tab *Actions* → Artefakt *AutoKey*).
Ein Tag wie `v1.0.0` erzeugt zusätzlich ein Release mit der `.exe` zum Download.
