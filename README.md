# AutoKey

Kostenloses Windows-Programm, das Tasten und Maustasten automatisch für dich drückt –
aufgezeichnet oder als Ablauf zusammengestellt, in festen Abständen, ab einer bestimmten Uhrzeit
oder dauerhaft gehalten. Gedacht für monotone, sich wiederholende Aufgaben.

- **Eine einzige Datei**: `AutoKey.exe` (ca. 150 KB), keine Installation nötig
- Läuft unter **Windows 10 und 11** (nutzt das dort eingebaute .NET Framework 4.8)
- Deutsche Oberfläche, Einstellungen und Profile werden automatisch gespeichert

## Download & Start

1. [`release/AutoKey.exe`](release/AutoKey.exe) öffnen und rechts oben auf **Download** (Pfeil-Symbol) klicken.
2. Die Datei irgendwo ablegen (z. B. Desktop) und per Doppelklick starten.

> **Windows-Warnung beim ersten Start?** Da das Programm nicht kostenpflichtig digital signiert ist,
> zeigt Windows SmartScreen evtl. „Der Computer wurde durch Windows geschützt“.
> Dann auf **Weitere Informationen → Trotzdem ausführen** klicken.

## So geht's in 30 Sekunden

1. **● Aufzeichnen …** klicken, ins Spiel wechseln und die Aufgabe **einmal vorführen**
   (Tasten, Mausklicks, Mausrad). Mit **F6** beenden.
2. Unter **Spiel / Zielfenster** das Spiel auswählen, damit nichts in andere Fenster geht.
3. **Wiederholen alle** einstellen (z. B. 20 Sekunden) und auf **Start** klicken – oder **F6** drücken.

AutoKey wiederholt den Ablauf, bis du **F6** drückst, die eingestellte Anzahl erreicht ist
oder die Stoppzeit kommt.

## Funktionen

### Ablauf (Schritte)

Ein Ablauf besteht aus Schritten, die nacheinander ausgeführt werden:

```
1.  1             (50 ms)   →  800 ms warten
2.  Maus links    (50 ms)   bei 960,540   →  2 s warten
3.  Text „/sit“   (20 ms/Zeichen)
4.  Enter         (50 ms)
```

| Art | Einstellungen |
|---|---|
| **Taste / Maustaste** | Taste aus der Liste oder per **Aufnehmen …** (auch Strg+S usw.); Maustasten links/rechts/Mitte/Seitentasten; **Haltedauer** in ms (bis 10 Minuten – z. B. zum Gedrückthalten beim Abbauen); bei Maustasten optional eine **feste Bildschirmposition** |
| **Text eintippen** | Tippt einen Text Zeichen für Zeichen, z. B. einen Chat-Befehl; Tippgeschwindigkeit einstellbar |
| **Mausrad drehen** | Anzahl Rasten nach oben oder unten, optional an fester Position |

Jeder Schritt hat eine Wartezeit **Danach warten** bis zum nächsten Schritt.
Schritte lassen sich hinzufügen, entfernen und verschieben (auch mit Entf, Einfg und Strg+Pfeiltasten in der Liste).

**Mausposition aufnehmen:** auf **Position (3 s)** klicken und die Maus innerhalb von 3 Sekunden an die
gewünschte Stelle bewegen.

### ● Aufzeichnen

Statt Schritte von Hand anzulegen, kannst du die Aufgabe **einmal vorführen**: AutoKey zeichnet Tastendrücke,
Mausklicks und Mausrad mit ihren Zeitabständen auf und macht daraus die Schritte. Danach lassen sich
Haltedauern und Wartezeiten noch anpassen. Die Zeit läuft ab der ersten Eingabe; beendet wird mit der
Start/Stopp-Taste oder über das kleine Aufnahmefenster.

### Wann?

| Einstellung | Bedeutung |
|---|---|
| **Modus** | *Ablauf wiederholt ausführen* – oder *Tasten dauerhaft gedrückt halten* (z. B. Auto-Laufen mit W + Shift, bis du stoppst) |
| **Wiederholen alle** | Abstand zwischen zwei Durchläufen (ms bis Stunden) |
| **Zufällig variieren** | Abstände, Haltedauern und Pausen um ± x % zufällig verändern – wirkt weniger mechanisch |
| **Wiederholungen** | Nach so vielen Durchläufen stoppen – `0` = unbegrenzt |
| **Beginnen** | *nach X Sekunden* oder *um HH:MM:SS Uhr* |
| **Automatisch stoppen** | Optional zu einer bestimmten Uhrzeit beenden |

### Spiel / Zielfenster

Unter **Nur senden an** das Spiel auswählen. Dann gilt:

- Eingaben gehen **nur an dieses Spiel** – wechselst du per Alt+Tab z. B. in den Browser oder Discord,
  **pausiert AutoKey automatisch** (gedrückte Tasten werden losgelassen) und macht weiter, sobald du zurück im Spiel bist.
- Wird das Spiel beendet, **stoppt AutoKey** von selbst.
- **Scancode senden** einschalten, wenn ein Spiel die Tasten sonst nicht annimmt.

### Profile

Oben **Speichern unter …** sichert Ablauf, Zeitplan und Zielfenster unter einem Namen (z. B. „Angeln“, „Farmen“).
Über die Auswahlliste lädst du ein Profil mit einem Klick wieder. **Profilordner öffnen** im Menü des
Symbols neben der Uhr – Profile sind einfache `.ini`-Dateien, die sich kopieren und weitergeben lassen.

### Bedienung im Spiel

| Option | Bedeutung |
|---|---|
| **Start/Stopp-Taste** | Standard **F6**, änderbar – auch Kombinationen wie **Strg+F6** oder **Alt+Q**, falls das Spiel F6 selbst benutzt. Wirkt in jedem Programm. |
| **Pause-Taste** | Optional (z. B. F8): unterbricht den Ablauf, ohne die Zähler zurückzusetzen; nochmal drücken macht weiter |
| **Signalton** | Kurzer Ton bei Start, Stopp und Pause – so hörst du im Spiel, dass AutoKey reagiert hat |
| **Unsichtbar im Hintergrund** | Das Fenster verschwindet beim Start; ein **grüner Punkt** am Symbol neben der Uhr zeigt, dass AutoKey läuft (Rechtsklick: Start/Stopp, Pause, Fenster, Beenden) |
| **Immer im Vordergrund / Infobereich / Benachrichtigungen / Standby verhindern** | wie gewohnt |

Die Fenstertitelzeile zeigt die Laufzeit, die Statuszeile Durchläufe, nächsten Schritt und Countdown.

**Wichtig:** Ohne gewähltes Zielfenster geht die Eingabe immer an das **gerade aktive Fenster** –
die Statuszeile zeigt einen Countdown bis zum Start.

### Beispiele

- **Angeln:** Aufzeichnen → im Spiel einmal auswerfen, warten, einholen → F6. Dann „Nur senden an“ das Spiel,
  Wiederholen alle `1 Sekunde`, Start.
- **Alle 30 Sekunden Leertaste, endlos:** ein Schritt `Leertaste`, Wiederholen alle `30 Sekunden`, Wiederholungen `0`.
- **Auto-Laufen:** Schritte `W` und `Shift`, Modus *dauerhaft gedrückt halten*.
- **Chat-Befehl alle 5 Minuten:** Schritt `Enter` → Schritt Text `/dance` → Schritt `Enter`; Wiederholen alle `5 Minuten`.
- **Einmal um 14:30 Uhr Enter drücken:** Schritt `Enter`, Wiederholungen `1`, Beginnen `um 14:30:00`.

### Startoptionen (für Verknüpfungen oder die Windows-Aufgabenplanung)

```
AutoKey.exe /start                  sofort mit den zuletzt gespeicherten Einstellungen loslegen
AutoKey.exe /tray                   unsichtbar im Infobereich starten
AutoKey.exe /profile:Angeln /start  Profil „Angeln“ laden und sofort starten
```

Wird AutoKey ein zweites Mal gestartet, holt es einfach das vorhandene Fenster nach vorn.

### Portabler Modus (USB-Stick)

Liegt neben der `AutoKey.exe` eine (leere) Datei namens **`portable.txt`**, werden Einstellungen, Profile und
Protokoll neben der EXE gespeichert statt in `%APPDATA%\AutoKey\`.

## Hinweise

- Läuft das Zielprogramm **als Administrator**, muss auch AutoKey als Administrator gestartet werden
  (Rechtsklick → *Als Administrator ausführen*), sonst blockiert Windows die Eingaben. AutoKey zeigt das dann an.
- Ein **gesperrter PC** (Win+L) nimmt keine Eingaben an.
- Bei Problemen: Fehler werden in `autokey.log` im Einstellungsordner protokolliert (Pfad unter „Über AutoKey“).
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
Ein Tag wie `v1.2.0` erzeugt zusätzlich ein Release mit der `.exe` zum Download.
