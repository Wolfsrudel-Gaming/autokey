# AutoKey - drückt automatisch eine Taste in einem festgelegten Intervall
# bzw. ab einer festgelegten Uhrzeit. Läuft mit Windows-Bordmitteln
# (PowerShell + Windows Forms), keine Installation nötig.
#
# Start: Doppelklick auf "AutoKey starten.bat"
# Start/Stopp jederzeit auch mit F6 (global, auch wenn ein anderes Fenster aktiv ist).

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not ([System.Management.Automation.PSTypeName]'AutoKeyNative').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class AutoKeyNative {
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP       = 0x0002;

    private const int VK_SHIFT   = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU    = 0x12; // Alt

    private static bool IsExtended(int vk) {
        switch (vk) {
            case 0x21: case 0x22: case 0x23: case 0x24: // Bild auf/ab, Ende, Pos1
            case 0x25: case 0x26: case 0x27: case 0x28: // Pfeiltasten
            case 0x2D: case 0x2E:                       // Einfg, Entf
            case 0x6F:                                  // Num /
            case 0x90:                                  // Num-Lock
                return true;
        }
        return false;
    }

    private static void Send(int vk, bool up) {
        uint flags = 0;
        if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        if (up) flags |= KEYEVENTF_KEYUP;
        byte scan = (byte)MapVirtualKey((uint)vk, 0);
        keybd_event((byte)vk, scan, flags, UIntPtr.Zero);
    }

    public static void Press(int vk, bool ctrl, bool alt, bool shift, int holdMs) {
        if (ctrl)  Send(VK_CONTROL, false);
        if (alt)   Send(VK_MENU, false);
        if (shift) Send(VK_SHIFT, false);
        Send(vk, false);
        if (holdMs > 0) Thread.Sleep(holdMs);
        Send(vk, true);
        if (shift) Send(VK_SHIFT, true);
        if (alt)   Send(VK_MENU, true);
        if (ctrl)  Send(VK_CONTROL, true);
    }

    public static bool IsKeyDown(int vk) {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }
}
'@
}

# ---------------------------------------------------------------- Einstellungen
$HotkeyVk   = 0x75   # F6 = Start/Stopp
$HoldMs     = 30     # wie lange die Taste gedrückt gehalten wird (ms)
$MinInterval = 50    # kleinstes erlaubtes Intervall (ms)

# ---------------------------------------------------------------- Tastenliste
$keys = [ordered]@{}
$keys['Leertaste']             = 0x20
$keys['Enter']                 = 0x0D
$keys['Tab']                   = 0x09
$keys['Esc']                   = 0x1B
$keys['Rücktaste (Backspace)'] = 0x08
$keys['Pfeil hoch']            = 0x26
$keys['Pfeil runter']          = 0x28
$keys['Pfeil links']           = 0x25
$keys['Pfeil rechts']          = 0x27
$keys['Bild auf']              = 0x21
$keys['Bild ab']               = 0x22
$keys['Pos1']                  = 0x24
$keys['Ende']                  = 0x23
$keys['Einfg']                 = 0x2D
$keys['Entf']                  = 0x2E
for ($i = 1; $i -le 12; $i++) {
    $vk = 0x6F + $i
    if ($vk -ne $HotkeyVk) { $keys["F$i"] = $vk }   # F6 ist der Start/Stopp-Hotkey
}
for ($c = [int][char]'A'; $c -le [int][char]'Z'; $c++) { $keys[[string][char]$c] = $c }
for ($d = 0; $d -le 9; $d++) { $keys["$d"] = 0x30 + $d }
for ($d = 0; $d -le 9; $d++) { $keys["Num $d"] = 0x60 + $d }

# ---------------------------------------------------------------- Zustand
$state = @{
    Running       = $false
    Next          = [datetime]::MinValue
    IntervalMs    = 1000.0
    MaxCount      = 0
    Presses       = 0
    Vk            = 0x20
    Ctrl          = $false
    Alt           = $false
    Shift         = $false
    HotkeyWasDown = $false
    LastStatus    = ''
}

# ---------------------------------------------------------------- Oberfläche
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.Text = 'AutoKey – automatischer Tastendruck'
$form.ClientSize = New-Object System.Drawing.Size(400, 345)
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

$panel = New-Object System.Windows.Forms.Panel
$panel.Location = New-Object System.Drawing.Point(0, 0)
$panel.Size = New-Object System.Drawing.Size(400, 204)
$form.Controls.Add($panel)

function New-Label([string]$text, [int]$x, [int]$y, [int]$w = 110) {
    $l = New-Object System.Windows.Forms.Label
    $l.Text = $text
    $l.Location = New-Object System.Drawing.Point($x, $y)
    $l.Size = New-Object System.Drawing.Size($w, 22)
    $l.TextAlign = 'MiddleLeft'
    $panel.Controls.Add($l)
    return $l
}

# Taste
[void](New-Label 'Taste:' 15 15)
$cmbKey = New-Object System.Windows.Forms.ComboBox
$cmbKey.DropDownStyle = 'DropDownList'
$cmbKey.Location = New-Object System.Drawing.Point(130, 15)
$cmbKey.Size = New-Object System.Drawing.Size(170, 22)
$cmbKey.MaxDropDownItems = 20
foreach ($k in $keys.Keys) { [void]$cmbKey.Items.Add($k) }
$cmbKey.SelectedIndex = 0
$panel.Controls.Add($cmbKey)

# Zusatztasten
[void](New-Label 'Zusatztasten:' 15 47)
$chkCtrl = New-Object System.Windows.Forms.CheckBox
$chkCtrl.Text = 'Strg'
$chkCtrl.Location = New-Object System.Drawing.Point(130, 47)
$chkCtrl.Size = New-Object System.Drawing.Size(60, 22)
$panel.Controls.Add($chkCtrl)
$chkAlt = New-Object System.Windows.Forms.CheckBox
$chkAlt.Text = 'Alt'
$chkAlt.Location = New-Object System.Drawing.Point(195, 47)
$chkAlt.Size = New-Object System.Drawing.Size(55, 22)
$panel.Controls.Add($chkAlt)
$chkShift = New-Object System.Windows.Forms.CheckBox
$chkShift.Text = 'Shift'
$chkShift.Location = New-Object System.Drawing.Point(255, 47)
$chkShift.Size = New-Object System.Drawing.Size(65, 22)
$panel.Controls.Add($chkShift)

# Intervall
[void](New-Label 'Alle:' 15 80)
$numInterval = New-Object System.Windows.Forms.NumericUpDown
$numInterval.Location = New-Object System.Drawing.Point(130, 80)
$numInterval.Size = New-Object System.Drawing.Size(80, 22)
$numInterval.Minimum = 1
$numInterval.Maximum = 1000000
$numInterval.Value = 5
$panel.Controls.Add($numInterval)
$cmbUnit = New-Object System.Windows.Forms.ComboBox
$cmbUnit.DropDownStyle = 'DropDownList'
$cmbUnit.Location = New-Object System.Drawing.Point(215, 80)
$cmbUnit.Size = New-Object System.Drawing.Size(115, 22)
[void]$cmbUnit.Items.AddRange(@('Millisekunden', 'Sekunden', 'Minuten', 'Stunden'))
$cmbUnit.SelectedIndex = 1
$panel.Controls.Add($cmbUnit)

# Wiederholungen
[void](New-Label 'Wiederholungen:' 15 113)
$numCount = New-Object System.Windows.Forms.NumericUpDown
$numCount.Location = New-Object System.Drawing.Point(130, 113)
$numCount.Size = New-Object System.Drawing.Size(80, 22)
$numCount.Minimum = 0
$numCount.Maximum = 10000000
$numCount.Value = 0
$panel.Controls.Add($numCount)
[void](New-Label '(0 = unbegrenzt)' 215 113 150)

# Erster Tastendruck: nach Verzögerung oder zu einer Uhrzeit
[void](New-Label 'Erster Druck:' 15 146)
$rbDelay = New-Object System.Windows.Forms.RadioButton
$rbDelay.Text = 'nach'
$rbDelay.Location = New-Object System.Drawing.Point(130, 146)
$rbDelay.Size = New-Object System.Drawing.Size(55, 22)
$rbDelay.Checked = $true
$panel.Controls.Add($rbDelay)
$numDelay = New-Object System.Windows.Forms.NumericUpDown
$numDelay.Location = New-Object System.Drawing.Point(190, 146)
$numDelay.Size = New-Object System.Drawing.Size(60, 22)
$numDelay.Minimum = 0
$numDelay.Maximum = 86400
$numDelay.Value = 5
$panel.Controls.Add($numDelay)
[void](New-Label 'Sekunden' 255 146 80)

$rbTime = New-Object System.Windows.Forms.RadioButton
$rbTime.Text = 'um'
$rbTime.Location = New-Object System.Drawing.Point(130, 176)
$rbTime.Size = New-Object System.Drawing.Size(55, 22)
$panel.Controls.Add($rbTime)
$dtpTime = New-Object System.Windows.Forms.DateTimePicker
$dtpTime.Format = 'Custom'
$dtpTime.CustomFormat = 'HH:mm:ss'
$dtpTime.ShowUpDown = $true
$dtpTime.Location = New-Object System.Drawing.Point(190, 176)
$dtpTime.Size = New-Object System.Drawing.Size(90, 22)
$dtpTime.Value = (Get-Date).Date.AddHours((Get-Date).Hour).AddMinutes((Get-Date).Minute + 5)
$panel.Controls.Add($dtpTime)
[void](New-Label 'Uhr' 285 176 60)
$dtpTime.Add_ValueChanged({ $rbTime.Checked = $true })
$numDelay.Add_ValueChanged({ $rbDelay.Checked = $true })

# Immer im Vordergrund
$chkTop = New-Object System.Windows.Forms.CheckBox
$chkTop.Text = 'Fenster immer im Vordergrund'
$chkTop.Location = New-Object System.Drawing.Point(15, 208)
$chkTop.Size = New-Object System.Drawing.Size(250, 22)
$chkTop.Add_CheckedChanged({ $form.TopMost = $chkTop.Checked })
$form.Controls.Add($chkTop)   # bleibt auch während des Laufs bedienbar

# Start / Stopp
$btnStart = New-Object System.Windows.Forms.Button
$btnStart.Text = 'Start  (F6)'
$btnStart.Location = New-Object System.Drawing.Point(15, 242)
$btnStart.Size = New-Object System.Drawing.Size(180, 34)
$form.Controls.Add($btnStart)

$btnStop = New-Object System.Windows.Forms.Button
$btnStop.Text = 'Stopp  (F6)'
$btnStop.Location = New-Object System.Drawing.Point(205, 242)
$btnStop.Size = New-Object System.Drawing.Size(180, 34)
$btnStop.Enabled = $false
$form.Controls.Add($btnStop)

$lblStatus = New-Object System.Windows.Forms.Label
$lblStatus.Location = New-Object System.Drawing.Point(15, 288)
$lblStatus.Size = New-Object System.Drawing.Size(370, 48)
$lblStatus.Text = "Bereit. Einstellungen wählen und Start drücken.`nDer Tastendruck geht an das jeweils aktive Fenster."
$form.Controls.Add($lblStatus)

# ---------------------------------------------------------------- Logik
function Format-Remaining([TimeSpan]$ts) {
    $secs = [math]::Max(0, [math]::Ceiling($ts.TotalSeconds))
    $t = [TimeSpan]::FromSeconds($secs)
    return '{0:00}:{1:00}:{2:00}' -f [math]::Floor($t.TotalHours), $t.Minutes, $t.Seconds
}

function Set-Status([string]$text) {
    if ($state.LastStatus -ne $text) {
        $lblStatus.Text = $text
        $state.LastStatus = $text
    }
}

function Stop-AutoKey([string]$message) {
    $state.Running = $false
    $panel.Enabled = $true
    $btnStart.Enabled = $true
    $btnStop.Enabled = $false
    Set-Status $message
}

function Start-AutoKey {
    $factor = switch ($cmbUnit.SelectedIndex) { 0 { 1 } 1 { 1000 } 2 { 60000 } default { 3600000 } }
    $intervalMs = [double]$numInterval.Value * $factor
    if ($intervalMs -lt $MinInterval) {
        [void][System.Windows.Forms.MessageBox]::Show(
            "Das Intervall muss mindestens $MinInterval Millisekunden betragen.",
            'AutoKey',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
        return
    }

    $now = Get-Date
    if ($rbTime.Checked) {
        $first = $now.Date + $dtpTime.Value.TimeOfDay
        if ($first -le $now) { $first = $first.AddDays(1) }   # Uhrzeit schon vorbei -> morgen
    } else {
        $first = $now.AddSeconds([double]$numDelay.Value)
    }

    $state.Vk         = $keys[[string]$cmbKey.SelectedItem]
    $state.Ctrl       = $chkCtrl.Checked
    $state.Alt        = $chkAlt.Checked
    $state.Shift      = $chkShift.Checked
    $state.IntervalMs = $intervalMs
    $state.MaxCount   = [int64]$numCount.Value
    $state.Presses    = 0
    $state.Next       = $first
    $state.Running    = $true

    $panel.Enabled = $false
    $btnStart.Enabled = $false
    $btnStop.Enabled = $true
}

function Switch-AutoKey {
    if ($state.Running) { Stop-AutoKey "Gestoppt. Ausgeführte Tastendrücke: $($state.Presses)" }
    else { Start-AutoKey }
}

$btnStart.Add_Click({ Start-AutoKey })
$btnStop.Add_Click({ Stop-AutoKey "Gestoppt. Ausgeführte Tastendrücke: $($state.Presses)" })

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 15
$timer.Add_Tick({
    # Globaler Hotkey (F6): nur auf das Herunterdrücken reagieren
    $hotDown = [AutoKeyNative]::IsKeyDown($HotkeyVk)
    if ($hotDown -and -not $state.HotkeyWasDown) { Switch-AutoKey }
    $state.HotkeyWasDown = $hotDown

    if (-not $state.Running) { return }

    $now = Get-Date
    if ($now -ge $state.Next) {
        [AutoKeyNative]::Press($state.Vk, $state.Ctrl, $state.Alt, $state.Shift, $HoldMs)
        $state.Presses++

        if ($state.MaxCount -gt 0 -and $state.Presses -ge $state.MaxCount) {
            Stop-AutoKey "Fertig. Ausgeführte Tastendrücke: $($state.Presses)"
            return
        }

        # Nächsten Zeitpunkt ohne Drift berechnen; bei Verzug nicht "nachholen"
        $state.Next = $state.Next.AddMilliseconds($state.IntervalMs)
        if ($state.Next -le $now) { $state.Next = $now.AddMilliseconds($state.IntervalMs) }
    }

    $remaining = Format-Remaining ($state.Next - $now)
    if ($state.Presses -eq 0) {
        Set-Status ("Wartet auf ersten Tastendruck um {0:HH:mm:ss} (noch {1}).`nJetzt das Zielfenster anklicken!" -f $state.Next, $remaining)
    } else {
        $of = if ($state.MaxCount -gt 0) { " von $($state.MaxCount)" } else { '' }
        Set-Status ("Läuft – Tastendrücke: $($state.Presses)$of`nNächster Druck in $remaining")
    }
})

$form.Add_Shown({ $timer.Start() })
$form.Add_FormClosing({ $timer.Stop(); $timer.Dispose() })

[void]$form.ShowDialog()
$form.Dispose()
