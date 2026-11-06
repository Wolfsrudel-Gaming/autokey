using System;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace AutoKey
{
    internal sealed class MainForm : Form
    {
        private const int HotkeyId = 0xA0C1;
        private const int MinIntervalMs = 20;

        private static readonly string AppVersion =
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        private readonly Settings settings;
        private readonly bool startImmediately;
        private readonly bool startInTray;
        private readonly Random random = new Random();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 10 };
        private readonly Icon appIcon;

        // Oberfläche
        private GroupBox grpKey, grpSchedule, grpOptions;
        private ComboBox cmbKey, cmbUnit, cmbHotkey;
        private Button btnCapture, btnStart, btnStop;
        private CheckBox chkCtrl, chkAlt, chkShift, chkScanCode, chkStop, chkTopMost, chkTray, chkPreventSleep;
        private NumericUpDown numHold, numInterval, numJitter, numRepeat, numDelay;
        private RadioButton rbDelay, rbTime;
        private DateTimePicker dtpStart, dtpStop;
        private Label lblStatus;
        private NotifyIcon tray;
        private ToolStripMenuItem trayToggle;

        // Zustand
        private bool running, holding, lastPressOk, capturing, hotkeyRegistered, closing;
        private Keys runKey;
        private bool runCtrl, runAlt, runShift, runScanCode;
        private double runIntervalMs;
        private int runJitter, runHoldMs;
        private long runRepeat, presses;
        private DateTime nextDueUtc, releaseUtc;
        private DateTime? stopUtc;
        private string sendWarning;
        private string lastStatus = string.Empty, lastTrayText = string.Empty;

        public MainForm(bool startImmediately, bool startInTray)
        {
            this.startImmediately = startImmediately;
            this.startInTray = startInTray;
            settings = Settings.Load();
            appIcon = LoadAppIcon();

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "AutoKey " + AppVersion;
            Icon = appIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            BuildUi();
            BuildTray();
            ApplySettingsToUi();

            ResumeLayout(false);
            PerformLayout();

            timer.Tick += OnTick;
            SetStatus("Bereit. Einstellungen wählen und auf Start klicken.");
        }

        // ------------------------------------------------------------------ Aufbau der Oberfläche
        // Feste Positionen (wie vom Visual-Studio-Designer erzeugt) bezogen auf 96 DPI;
        // bei höherer Bildschirmskalierung skaliert Windows Forms alles automatisch (AutoScaleMode.Dpi).

        private const int FieldX = 150;   // Spalte der Eingabefelder innerhalb einer Gruppe
        private const int FirstRow = 24;  // erste Zeile unterhalb der Gruppenüberschrift
        private const int RowHeight = 30;

        private void BuildUi()
        {
            const int formWidth = 480;
            const int groupX = 10;
            const int groupWidth = formWidth - 2 * groupX;
            int y = 8;

            // --- Welche Taste?
            grpKey = NewGroup("Welche Taste?", groupX, y, groupWidth, FirstRow + 4 * RowHeight + 4);
            cmbKey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, MaxDropDownItems = 20 };
            foreach (Keys k in KeyNames.CommonKeys) cmbKey.Items.Add(new KeyItem(k));
            btnCapture = new Button { Text = "Aufnehmen …", Size = new Size(120, 25) };
            btnCapture.Click += (s, e) => SetCapturing(!capturing);
            chkCtrl = NewCheck("Strg");
            chkAlt = NewCheck("Alt");
            chkShift = NewCheck("Shift");
            numHold = NewNumber(1, 10_000, 70);
            chkScanCode = NewCheck("Scancode senden (für Spiele empfohlen)");

            AddLabel(grpKey, "Taste:", 0);
            AddField(grpKey, cmbKey, FieldX, 0);
            AddField(grpKey, btnCapture, FieldX + 180, 0);
            AddLabel(grpKey, "Mit gedrückt:", 1);
            AddField(grpKey, chkCtrl, FieldX, 1);
            AddField(grpKey, chkAlt, FieldX + 60, 1);
            AddField(grpKey, chkShift, FieldX + 115, 1);
            AddLabel(grpKey, "Haltedauer:", 2);
            AddField(grpKey, numHold, FieldX, 2);
            AddNote(grpKey, "Millisekunden", FieldX + 76, 2);
            AddField(grpKey, chkScanCode, FieldX, 3);
            y = grpKey.Bottom + 8;

            // --- Wann?
            grpSchedule = NewGroup("Wann?", groupX, y, groupWidth, FirstRow + 6 * RowHeight + 4);
            numInterval = NewNumber(1, 1_000_000, 70);
            cmbUnit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
            cmbUnit.Items.AddRange(new object[] { "Millisekunden", "Sekunden", "Minuten", "Stunden" });
            numJitter = NewNumber(0, 90, 55);
            numRepeat = NewNumber(0, 100_000_000, 90);
            numRepeat.ThousandsSeparator = true;
            rbDelay = NewRadio("nach");
            numDelay = NewNumber(0, 86_400, 60);
            rbTime = NewRadio("um");
            dtpStart = NewTimePicker();
            chkStop = NewCheck("um");
            dtpStop = NewTimePicker();

            AddLabel(grpSchedule, "Drücken alle:", 0);
            AddField(grpSchedule, numInterval, FieldX, 0);
            AddField(grpSchedule, cmbUnit, FieldX + 76, 0);
            AddLabel(grpSchedule, "Zufällig variieren:", 1);
            AddNote(grpSchedule, "±", FieldX, 1);
            AddField(grpSchedule, numJitter, FieldX + 16, 1);
            AddNote(grpSchedule, "%   (wirkt menschlicher)", FieldX + 77, 1);
            AddLabel(grpSchedule, "Wiederholungen:", 2);
            AddField(grpSchedule, numRepeat, FieldX, 2);
            AddNote(grpSchedule, "(0 = unbegrenzt)", FieldX + 96, 2);
            AddLabel(grpSchedule, "Erster Tastendruck:", 3);
            AddField(grpSchedule, rbDelay, FieldX, 3);
            AddField(grpSchedule, numDelay, FieldX + 55, 3);
            AddNote(grpSchedule, "Sekunden nach dem Start", FieldX + 121, 3);
            AddField(grpSchedule, rbTime, FieldX, 4);
            AddField(grpSchedule, dtpStart, FieldX + 55, 4);
            AddNote(grpSchedule, "Uhr", FieldX + 146, 4);
            AddLabel(grpSchedule, "Automatisch stoppen:", 5);
            AddField(grpSchedule, chkStop, FieldX, 5);
            AddField(grpSchedule, dtpStop, FieldX + 55, 5);
            AddNote(grpSchedule, "Uhr", FieldX + 146, 5);
            y = grpSchedule.Bottom + 8;

            // Die beiden Optionen liegen nicht im selben Container -> Gruppierung selbst übernehmen.
            rbDelay.CheckedChanged += (s, e) => { if (rbDelay.Checked) rbTime.Checked = false; };
            rbTime.CheckedChanged += (s, e) => { if (rbTime.Checked) rbDelay.Checked = false; };
            numDelay.ValueChanged += (s, e) => rbDelay.Checked = true;
            dtpStart.ValueChanged += (s, e) => rbTime.Checked = true;
            dtpStop.ValueChanged += (s, e) => chkStop.Checked = true;

            // --- Optionen
            grpOptions = NewGroup("Optionen", groupX, y, groupWidth, FirstRow + RowHeight + 3 * 26 + 6);
            cmbHotkey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
            foreach (Keys k in KeyNames.HotkeyKeys) cmbHotkey.Items.Add(new KeyItem(k));
            cmbHotkey.SelectedIndexChanged += (s, e) => RegisterHotkey();
            chkTopMost = NewCheck("Fenster immer im Vordergrund");
            chkTopMost.CheckedChanged += (s, e) => TopMost = chkTopMost.Checked;
            chkTray = NewCheck("Beim Minimieren in den Infobereich (neben der Uhr)");
            chkPreventSleep = NewCheck("Standby verhindern, solange AutoKey läuft");

            AddLabel(grpOptions, "Start/Stopp-Taste:", 0);
            AddField(grpOptions, cmbHotkey, FieldX, 0);
            AddNote(grpOptions, "(funktioniert in jedem Programm)", FieldX + 96, 0);
            int optY = FirstRow + RowHeight;
            foreach (CheckBox c in new[] { chkTopMost, chkTray, chkPreventSleep })
            {
                c.Location = new Point(12, optY);
                grpOptions.Controls.Add(c);
                optY += 26;
            }
            y = grpOptions.Bottom + 10;

            // --- Start / Stopp
            int buttonWidth = (groupWidth - 10) / 2;
            btnStart = new Button { Bounds = new Rectangle(groupX, y, buttonWidth, 38) };
            btnStart.Font = new Font(Font, FontStyle.Bold);
            btnStart.Click += (s, e) => StartRun();
            btnStop = new Button { Bounds = new Rectangle(groupX + buttonWidth + 10, y, buttonWidth, 38), Enabled = false };
            btnStop.Click += (s, e) => StopRun("Gestoppt.");
            y = btnStart.Bottom + 8;

            // --- Statusanzeige (zweizeilig, damit auch längere Hinweise ganz lesbar sind)
            lblStatus = new Label
            {
                Bounds = new Rectangle(groupX, y, groupWidth, 44),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0),
            };
            y = lblStatus.Bottom + 10;

            Controls.AddRange(new Control[] { grpKey, grpSchedule, grpOptions, btnStart, btnStop, lblStatus });
            ClientSize = new Size(formWidth, y);
        }

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();
            trayToggle = new ToolStripMenuItem("Start", null, (s, e) => ToggleRun());
            menu.Items.Add(trayToggle);
            menu.Items.Add("Fenster anzeigen", null, (s, e) => RestoreFromTray());
            menu.Items.Add("Über AutoKey", null, (s, e) => ShowAbout());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Beenden", null, (s, e) => Close());

            tray = new NotifyIcon
            {
                Icon = new Icon(appIcon, SystemInformation.SmallIconSize),
                Text = "AutoKey",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.DoubleClick += (s, e) => RestoreFromTray();
        }

        private static GroupBox NewGroup(string title, int x, int y, int width, int height) =>
            new GroupBox { Text = title, Bounds = new Rectangle(x, y, width, height) };

        private static int RowY(int row) => FirstRow + row * RowHeight;

        /// <summary>Beschriftung in der linken Spalte, vertikal an den Eingabefeldern ausgerichtet.</summary>
        private static void AddLabel(Control parent, string text, int row) =>
            parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(12, RowY(row) + 4) });

        /// <summary>Kurzer Hinweistext hinter einem Eingabefeld.</summary>
        private static void AddNote(Control parent, string text, int x, int row) =>
            parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, RowY(row) + 4) });

        private static void AddField(Control parent, Control field, int x, int row)
        {
            int offset = field is CheckBox || field is RadioButton ? 2 : field is Button ? -1 : 0;
            field.Location = new Point(x, RowY(row) + offset);
            parent.Controls.Add(field);
        }

        private static CheckBox NewCheck(string text) => new CheckBox { Text = text, AutoSize = true };

        private static RadioButton NewRadio(string text) =>
            new RadioButton { Text = text, AutoSize = false, Size = new Size(52, 20) };

        private static NumericUpDown NewNumber(int min, int max, int width) =>
            new NumericUpDown { Minimum = min, Maximum = max, Width = width };

        private static DateTimePicker NewTimePicker() =>
            new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm:ss", ShowUpDown = true, Width = 85 };

        private static Icon LoadAppIcon()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutoKey.ico"))
                {
                    if (stream != null) return new Icon(stream);
                }
            }
            catch (Exception)
            {
                // Fallback unten
            }

            return SystemIcons.Application;
        }

        // ------------------------------------------------------------------ Einstellungen <-> Oberfläche

        private void ApplySettingsToUi()
        {
            SelectKey(settings.Key);
            chkCtrl.Checked = settings.Ctrl;
            chkAlt.Checked = settings.Alt;
            chkShift.Checked = settings.Shift;
            SetNumber(numHold, settings.HoldMs);
            chkScanCode.Checked = settings.ScanCode;

            SetNumber(numInterval, settings.Interval);
            cmbUnit.SelectedIndex = (int)settings.Unit;
            SetNumber(numJitter, settings.JitterPercent);
            SetNumber(numRepeat, settings.Repeat);
            SetNumber(numDelay, settings.StartDelaySeconds);
            dtpStart.Value = DateTime.Today + settings.StartTime;
            dtpStop.Value = DateTime.Today + settings.StopTime;
            if (settings.StartAtTime) rbTime.Checked = true; else rbDelay.Checked = true;
            chkStop.Checked = settings.StopAtTime;

            int hotkeyIndex = 0;
            for (int i = 0; i < cmbHotkey.Items.Count; i++)
            {
                if (((KeyItem)cmbHotkey.Items[i]).Key == settings.Hotkey) hotkeyIndex = i;
            }
            cmbHotkey.SelectedIndex = hotkeyIndex;
            chkTopMost.Checked = settings.TopMost;
            chkTray.Checked = settings.MinimizeToTray;
            chkPreventSleep.Checked = settings.PreventSleep;
            UpdateButtonTexts();
        }

        private void ReadSettingsFromUi()
        {
            settings.Key = SelectedKey;
            settings.Ctrl = chkCtrl.Checked;
            settings.Alt = chkAlt.Checked;
            settings.Shift = chkShift.Checked;
            settings.HoldMs = (int)numHold.Value;
            settings.ScanCode = chkScanCode.Checked;

            settings.Interval = (int)numInterval.Value;
            settings.Unit = (TimeUnit)Math.Max(0, cmbUnit.SelectedIndex);
            settings.JitterPercent = (int)numJitter.Value;
            settings.Repeat = (long)numRepeat.Value;
            settings.StartAtTime = rbTime.Checked;
            settings.StartDelaySeconds = (int)numDelay.Value;
            settings.StartTime = TruncateToSeconds(dtpStart.Value.TimeOfDay);
            settings.StopAtTime = chkStop.Checked;
            settings.StopTime = TruncateToSeconds(dtpStop.Value.TimeOfDay);

            settings.Hotkey = SelectedHotkey;
            settings.TopMost = chkTopMost.Checked;
            settings.MinimizeToTray = chkTray.Checked;
            settings.PreventSleep = chkPreventSleep.Checked;
        }

        private static TimeSpan TruncateToSeconds(TimeSpan t) => new TimeSpan(t.Hours, t.Minutes, t.Seconds);

        private static void SetNumber(NumericUpDown control, decimal value) =>
            control.Value = Math.Max(control.Minimum, Math.Min(control.Maximum, value));

        private Keys SelectedKey => (cmbKey.SelectedItem as KeyItem)?.Key ?? Keys.Space;

        private Keys SelectedHotkey => (cmbHotkey.SelectedItem as KeyItem)?.Key ?? Keys.F6;

        private void SelectKey(Keys key)
        {
            foreach (KeyItem item in cmbKey.Items)
            {
                if (item.Key == key)
                {
                    cmbKey.SelectedItem = item;
                    return;
                }
            }

            var added = new KeyItem(key);
            cmbKey.Items.Add(added);
            cmbKey.SelectedItem = added;
        }

        private void UpdateButtonTexts()
        {
            string hotkey = hotkeyRegistered ? "  (" + KeyNames.GetName(SelectedHotkey) + ")" : string.Empty;
            btnStart.Text = "▶  Start" + hotkey;
            btnStop.Text = "■  Stopp" + hotkey;
        }

        // ------------------------------------------------------------------ Taste aufnehmen

        private void SetCapturing(bool on)
        {
            capturing = on;
            btnCapture.Text = on ? "Taste drücken …" : "Aufnehmen …";
            SetStatus(on
                ? "Jetzt die gewünschte Taste drücken (auch mit Strg/Alt/Shift möglich) …"
                : "Bereit.");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (capturing)
            {
                Keys code = keyData & Keys.KeyCode;
                bool modifierOnly = code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu
                                    || code == Keys.LWin || code == Keys.RWin;
                if (!modifierOnly && code != Keys.None)
                {
                    SelectKey(code);
                    chkCtrl.Checked = (keyData & Keys.Control) != 0;
                    chkAlt.Checked = (keyData & Keys.Alt) != 0;
                    chkShift.Checked = (keyData & Keys.Shift) != 0;
                    SetCapturing(false);
                    SetStatus("Taste übernommen: " + DescribeKey(code, chkCtrl.Checked, chkAlt.Checked, chkShift.Checked));
                }

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static string DescribeKey(Keys key, bool ctrl, bool alt, bool shift)
        {
            string text = KeyNames.GetName(key);
            if (shift) text = "Shift+" + text;
            if (alt) text = "Alt+" + text;
            if (ctrl) text = "Strg+" + text;
            return text;
        }

        // ------------------------------------------------------------------ Globaler Hotkey

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkey();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotkey();
            base.OnHandleDestroyed(e);
        }

        private void RegisterHotkey()
        {
            UnregisterHotkey();
            if (!IsHandleCreated) return;

            try
            {
                hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId, NativeMethods.MOD_NOREPEAT, (uint)SelectedHotkey);
            }
            catch (Exception)
            {
                hotkeyRegistered = false;
            }

            UpdateButtonTexts();
            if (!hotkeyRegistered)
            {
                SetStatus("Die Taste " + KeyNames.GetName(SelectedHotkey)
                          + " wird schon von einem anderen Programm benutzt – bitte eine andere Start/Stopp-Taste wählen.");
            }
        }

        private void UnregisterHotkey()
        {
            if (!hotkeyRegistered) return;
            try
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            }
            catch (Exception)
            {
                // ignorieren
            }

            hotkeyRegistered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                if (!capturing) ToggleRun();
                return;
            }

            base.WndProc(ref m);
        }

        // ------------------------------------------------------------------ Start / Stopp

        private void ToggleRun()
        {
            if (running) StopRun("Gestoppt.");
            else StartRun();
        }

        private void StartRun()
        {
            if (running) return;
            if (capturing) SetCapturing(false);

            ReadSettingsFromUi();
            double intervalMs = settings.IntervalMilliseconds;

            if (settings.Key == settings.Hotkey && !settings.Ctrl && !settings.Alt && !settings.Shift)
            {
                Warn("Die Taste " + KeyNames.GetName(settings.Key) + " ist gleichzeitig die Start/Stopp-Taste.\n\n"
                     + "Bitte unter „Optionen“ eine andere Start/Stopp-Taste wählen.");
                return;
            }

            if (intervalMs < MinIntervalMs)
            {
                Warn("Das Intervall muss mindestens " + MinIntervalMs + " Millisekunden betragen.");
                return;
            }

            if (settings.HoldMs >= intervalMs)
            {
                Warn("Die Haltedauer (" + settings.HoldMs + " ms) muss kürzer sein als das Intervall.");
                return;
            }

            DateTime nowLocal = DateTime.Now;
            DateTime firstLocal;
            if (settings.StartAtTime)
            {
                firstLocal = nowLocal.Date + settings.StartTime;
                if (firstLocal <= nowLocal) firstLocal = firstLocal.AddDays(1); // Uhrzeit heute schon vorbei -> morgen
            }
            else
            {
                firstLocal = nowLocal.AddSeconds(settings.StartDelaySeconds);
            }

            stopUtc = null;
            if (settings.StopAtTime)
            {
                DateTime stopLocal = firstLocal.Date + settings.StopTime;
                if (stopLocal <= firstLocal) stopLocal = stopLocal.AddDays(1);
                stopUtc = stopLocal.ToUniversalTime();
            }

            runKey = settings.Key;
            runCtrl = settings.Ctrl;
            runAlt = settings.Alt;
            runShift = settings.Shift;
            runScanCode = settings.ScanCode;
            runIntervalMs = intervalMs;
            runJitter = settings.JitterPercent;
            runHoldMs = settings.HoldMs;
            runRepeat = settings.Repeat;
            presses = 0;
            holding = false;
            sendWarning = null;
            nextDueUtc = firstLocal.ToUniversalTime();
            running = true;

            settings.Save();
            if (settings.PreventSleep) NativeMethods.PreventSleep(true);
            SetRunningUi(true);
            timer.Start();
            UpdateRunStatus(DateTime.UtcNow);
        }

        private void StopRun(string message)
        {
            if (!running) return;

            timer.Stop();
            if (holding)
            {
                ReleaseKey();
                holding = false;
            }

            running = false;
            NativeMethods.PreventSleep(false);
            SetRunningUi(false);

            string text = message + " Ausgeführte Tastendrücke: " + presses.ToString("N0", CultureInfo.CurrentCulture) + ".";
            SetStatus(text);
            if (!Visible && !closing) tray.ShowBalloonTip(4000, "AutoKey", text, ToolTipIcon.Info);
        }

        private void SetRunningUi(bool isRunning)
        {
            grpKey.Enabled = !isRunning;
            grpSchedule.Enabled = !isRunning;
            cmbHotkey.Enabled = !isRunning;
            btnStart.Enabled = !isRunning;
            btnStop.Enabled = isRunning;
            trayToggle.Text = isRunning ? "Stopp" : "Start";
            Text = isRunning ? "AutoKey – läuft" : "AutoKey " + AppVersion;
            SetTrayText(isRunning ? "AutoKey – läuft" : "AutoKey – gestoppt");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!running) return;
            DateTime now = DateTime.UtcNow;

            if (holding && now >= releaseUtc)
            {
                ReleaseKey();
                holding = false;
                if (lastPressOk) presses++; // blockierte Tastendrücke nicht mitzählen
                SetTrayText("AutoKey – läuft (" + presses.ToString("N0", CultureInfo.CurrentCulture) + "×)");

                if (runRepeat > 0 && presses >= runRepeat)
                {
                    StopRun("Fertig.");
                    return;
                }
            }

            if (!holding && stopUtc.HasValue && now >= stopUtc.Value)
            {
                StopRun("Stoppzeit erreicht.");
                return;
            }

            if (!holding && now >= nextDueUtc)
            {
                PressKey();
                holding = true;
                releaseUtc = now.AddMilliseconds(runHoldMs);
                nextDueUtc = ComputeNextDue(nextDueUtc, now);
            }

            UpdateRunStatus(now);
        }

        private DateTime ComputeNextDue(DateTime previousDue, DateTime now)
        {
            double ms = runIntervalMs;
            if (runJitter > 0) ms *= 1.0 + (random.NextDouble() * 2.0 - 1.0) * runJitter / 100.0;
            ms = Math.Max(ms, MinIntervalMs);

            // Vom geplanten Zeitpunkt aus rechnen (kein Driften), aber verpasste Drücke nicht nachholen.
            DateTime next = previousDue.AddMilliseconds(ms);
            return next > now ? next : now.AddMilliseconds(ms);
        }

        private void PressKey()
        {
            bool ok;
            try
            {
                ok = KeySender.Down(runKey, runCtrl, runAlt, runShift, runScanCode);
            }
            catch (Exception)
            {
                ok = false;
            }

            lastPressOk = ok;
            sendWarning = ok
                ? null
                : "Windows hat den Tastendruck blockiert. Läuft das Zielprogramm als Administrator? Dann AutoKey ebenfalls als Administrator starten.";
        }

        private void ReleaseKey()
        {
            try
            {
                KeySender.Up(runKey, runCtrl, runAlt, runShift, runScanCode);
            }
            catch (Exception)
            {
                // ignorieren
            }
        }

        // ------------------------------------------------------------------ Status

        private void UpdateRunStatus(DateTime now)
        {
            if (sendWarning != null)
            {
                SetStatus(sendWarning);
                return;
            }

            string text;
            if (presses == 0 && !holding)
            {
                text = "Wartet – erster Tastendruck um " + nextDueUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture)
                       + " (in " + FormatRemaining(nextDueUtc - now) + "). Jetzt ins Zielfenster klicken!";
            }
            else
            {
                string count = presses.ToString("N0", CultureInfo.CurrentCulture);
                if (runRepeat > 0) count += " von " + runRepeat.ToString("N0", CultureInfo.CurrentCulture);
                text = "Läuft – " + count + " Tastendrücke – nächster in " + FormatRemaining(nextDueUtc - now);
            }

            if (stopUtc.HasValue)
                text += " – Stopp um " + stopUtc.Value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

            SetStatus(text);
        }

        private static string FormatRemaining(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            if (span.TotalSeconds < 10) return span.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";
            if (span.TotalSeconds < 60) return Math.Ceiling(span.TotalSeconds).ToString(CultureInfo.CurrentCulture) + " s";

            long seconds = (long)Math.Ceiling(span.TotalSeconds);
            return string.Format(CultureInfo.CurrentCulture, "{0:00}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60);
        }

        private void SetStatus(string text)
        {
            if (text == lastStatus) return;
            lastStatus = text;
            lblStatus.Text = text;
        }

        private void SetTrayText(string text)
        {
            if (text.Length > 63) text = text.Substring(0, 63); // Windows-Grenze für Tooltips im Infobereich
            if (text == lastTrayText) return;
            lastTrayText = text;
            tray.Text = text;
        }

        private void Warn(string message) =>
            MessageBox.Show(this, message, "AutoKey", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        private void ShowAbout()
        {
            MessageBox.Show(
                "AutoKey " + AppVersion + "\n\n"
                + "Drückt automatisch eine Taste – in festen Abständen oder zu einer bestimmten Uhrzeit.\n\n"
                + "Einstellungen werden gespeichert in:\n" + Settings.FilePath + "\n\n"
                + "Startoptionen:\n  /start  – sofort mit den gespeicherten Einstellungen loslegen\n  /tray   – minimiert im Infobereich starten",
                "Über AutoKey", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------ Fenster / Infobereich

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (startInTray)
            {
                WindowState = FormWindowState.Minimized;
                Hide();
            }

            if (startImmediately) StartRun();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && chkTray != null && chkTray.Checked)
            {
                Hide();
            }
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            StopRun("Beendet.");
            ReadSettingsFromUi();
            settings.Save();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                if (tray != null)
                {
                    tray.Visible = false;
                    tray.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
