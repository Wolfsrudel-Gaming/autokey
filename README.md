# AutoKey

Kostenloses Mini-Tool für Windows, das automatisch eine Taste für dich drückt –
in einem festen Intervall und/oder ab einer bestimmten Uhrzeit.

Keine Installation nötig: Es nutzt nur PowerShell, das in Windows 10/11 schon dabei ist.

## Start

1. Repository als ZIP herunterladen (**Code → Download ZIP**) und entpacken.
2. Doppelklick auf **`AutoKey starten.bat`**.

> Falls Windows beim ersten Start warnt („Der Computer wurde durch Windows geschützt“):
> auf **Weitere Informationen → Trotzdem ausführen** klicken. Alternativ vorher
> Rechtsklick auf die heruntergeladene ZIP → **Eigenschaften → Zulassen** anhaken.

## Bedienung

| Einstellung      | Bedeutung                                                                   |
|------------------|-----------------------------------------------------------------------------|
| **Taste**        | Welche Taste gedrückt wird (Leertaste, Enter, F-Tasten, A–Z, 0–9, Pfeile …) |
| **Zusatztasten** | Optional Strg / Alt / Shift mitdrücken (z. B. Strg+S)                       |
| **Alle**         | Intervall zwischen zwei Tastendrücken (ms, Sekunden, Minuten, Stunden)      |
| **Wiederholungen** | Nach so vielen Tastendrücken automatisch stoppen – `0` = unbegrenzt        |
| **Erster Druck** | *nach X Sekunden* (Zeit, um ins Zielfenster zu wechseln) **oder** *um HH:MM:SS Uhr* |

Dann **Start** klicken (oder **F6** drücken) und **ins Zielfenster klicken** – der
Tastendruck geht immer an das gerade aktive Fenster. **F6** stoppt wieder, egal welches
Fenster gerade aktiv ist.

### Beispiele

- *Alle 30 Sekunden Leertaste, endlos:* Taste `Leertaste`, Alle `30 Sekunden`, Wiederholungen `0`.
- *Einmal um 14:30 Uhr Enter drücken:* Taste `Enter`, Wiederholungen `1`, Erster Druck `um 14:30:00`.
- *Ab 8:00 Uhr jede Minute F5:* Taste `F5`, Alle `1 Minuten`, Erster Druck `um 08:00:00`.

Liegt die gewählte Uhrzeit heute schon in der Vergangenheit, wird morgen um diese Uhrzeit gestartet.

## Hinweise

- Der PC darf nicht gesperrt sein bzw. in den Standby gehen, sonst kommen keine Tastendrücke an.
- Läuft das Zielprogramm **als Administrator**, muss auch AutoKey als Administrator
  gestartet werden (Rechtsklick auf die `.bat` → *Als Administrator ausführen*).
- F6 ist für Start/Stopp reserviert und deshalb nicht als Taste auswählbar.
- Einige Spiele mit Anti-Cheat ignorieren oder verbieten simulierte Tastendrücke.
- Haltedauer, Hotkey und kleinstes Intervall lassen sich oben in `AutoKey.ps1` ändern.
- Fehlersuche: In einer Konsole `powershell -ExecutionPolicy Bypass -File AutoKey.ps1`
  ausführen, dann werden eventuelle Fehlermeldungen angezeigt.
