# AutoKey

Kostenloses Windows-Programm, das automatisch eine Taste für dich drückt –
in festen Abständen, eine bestimmte Anzahl Mal oder ab einer bestimmten Uhrzeit.

- **Eine einzige Datei**: `AutoKey.exe` (ca. 110 KB), keine Installation nötig
- Läuft unter **Windows 10 und 11** (nutzt das dort eingebaute .NET Framework 4.8)
- Deutsche Oberfläche, Einstellungen werden automatisch gespeichert

## Download & Start

1. [`release/AutoKey.exe`](release/AutoKey.exe) öffnen und rechts oben auf **Download** (Pfeil-Symbol) klicken.
2. Die Datei irgendwo ablegen (z. B. Desktop) und per Doppelklick starten.

> **Windows-Warnung beim ersten Start?** Da das Programm nicht kostenpflichtig digital signiert ist,
> zeigt Windows SmartScreen evtl. „Der Computer wurde durch Windows geschützt“.
> Dann auf **Weitere Informationen → Trotzdem ausführen** klicken.

## Funktionen

| Bereich | Einstellung | Bedeutung |
|---|---|---|
| **Welche Taste?** | Taste | Auswahl aus der Liste – oder **Aufnehmen …** klicken und die gewünschte Taste (auch Kombinationen wie Strg+S) drücken |
| | Mit gedrückt | Strg / Alt / Shift zusätzlich halten |
| | Haltedauer | Wie lange die Taste gedrückt bleibt (Millisekunden) |
| | Scancode senden | Für Spiele, die normale simulierte Tasten ignorieren |
| **Wann?** | Drücken alle | Intervall in Millisekunden, Sekunden, Minuten oder Stunden |
| | Zufällig variieren | Intervall um ± x % zufällig verändern (wirkt menschlicher) |
| | Wiederholungen | Nach so vielen Tastendrücken automatisch stoppen – `0` = unbegrenzt |
| | Erster Tastendruck | **nach X Sekunden** (Zeit, um ins Zielfenster zu wechseln) **oder um HH:MM:SS Uhr** |
| | Automatisch stoppen | Optional zu einer bestimmten Uhrzeit beenden |
| **Optionen** | Start/Stopp-Taste | Globale Taste (Standard **F6**), funktioniert auch, wenn ein anderes Programm aktiv ist |
| | Immer im Vordergrund | AutoKey-Fenster bleibt sichtbar |
| | Infobereich | Beim Minimieren neben die Uhr verschwinden (Rechtsklick auf das Symbol: Start/Stopp, Beenden) |
| | Standby verhindern | PC geht nicht in den Standby, solange AutoKey läuft |

**Wichtig:** Der Tastendruck geht immer an das **gerade aktive Fenster**. Also nach dem Start
ins Zielprogramm klicken – die Statuszeile zeigt einen Countdown bis zum ersten Tastendruck.

### Beispiele

- **Alle 30 Sekunden Leertaste, endlos:** Taste `Leertaste`, Drücken alle `30 Sekunden`, Wiederholungen `0`.
- **Einmal um 14:30 Uhr Enter drücken:** Taste `Enter`, Wiederholungen `1`, Erster Tastendruck `um 14:30:00`.
- **Von 8 bis 17 Uhr jede Minute F5:** Taste `F5`, Drücken alle `1 Minuten`, Erster Tastendruck `um 08:00:00`,
  Automatisch stoppen `um 17:00:00`.

Liegt die Startuhrzeit heute schon in der Vergangenheit, startet AutoKey morgen zu dieser Uhrzeit.

### Startoptionen (für Verknüpfungen oder die Windows-Aufgabenplanung)

```
AutoKey.exe /start    sofort mit den zuletzt gespeicherten Einstellungen loslegen
AutoKey.exe /tray     minimiert im Infobereich starten
```

Die Einstellungen liegen in `%APPDATA%\AutoKey\settings.ini`.

## Hinweise

- Läuft das Zielprogramm **als Administrator**, muss auch AutoKey als Administrator gestartet werden
  (Rechtsklick → *Als Administrator ausführen*), sonst blockiert Windows die Tastendrücke. AutoKey zeigt das dann an.
- Ein **gesperrter PC** (Win+L) nimmt keine Tastendrücke an.
- Einige Spiele mit Anti-Cheat ignorieren oder verbieten simulierte Tastendrücke – im Zweifel die Regeln des Spiels beachten.

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
