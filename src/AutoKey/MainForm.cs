using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace AutoKey
{
    internal sealed class MainForm : Form
    {
        private enum Phase
        {
            Waiting,  // wartet auf den nächsten Durchlauf
            Holding,  // Taste eines Schritts ist gedrückt
            Gap,      // Pause zwischen zwei Schritten
        }

        private const int HotkeyId = 0xA0C1;
        private const int MinIntervalMs = 20;
        private const int ResumeDelayMs = 300; // kurze Pause, nachdem das Zielfenster wieder aktiv geworden ist
        private const string NoProfile = "(kein Profil)";
        private const string BlockedMessage =
            "Windows hat die Eingabe blockiert. Läuft das Zielprogramm als Administrator? Dann AutoKey ebenfalls als Administrator starten.";

        private static readonly string AppVersion =
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        private readonly Settings settings;
        private readonly bool startImmediately;
        private readonly bool startInTray;
        private readonly Random random = new Random();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 10 };
        private readonly Icon appIcon;
        private Icon trayIdleIcon, trayRunningIcon;

        // Oberfläche
        private ComboBox cmbProfile, cmbKey, cmbWindow, cmbMode, cmbUnit, cmbHotkey;
        private Button btnProfileSave, btnProfileDelete, btnStepAdd, btnStepRemove, btnStepUp, btnStepDown;
        private Button btnCapture, btnRefreshWindows, btnStart, btnStop;
        private GroupBox grpSteps, grpTarget, grpSchedule, grpOptions;
        private ListBox lstSteps;
        private CheckBox chkCtrl, chkAlt, chkShift, chkScanCode, chkStop;
        private CheckBox chkTopMost, chkTray, chkHideOnStart, chkNotify, chkPreventSleep;
        private NumericUpDown numHold, numAfter, numInterval, numJitter, numRepeat, numDelay;
        private RadioButton rbDelay, rbTime;
        private DateTimePicker dtpStart, dtpStop;
        private Label lblStatus;
        private NotifyIcon tray;
        private ToolStripMenuItem trayToggle;

        // Bearbeitung
        private readonly List<Step> steps = new List<Step>();
        private bool updatingUi; // unterdrückt Ereignisse, während die Oberfläche per Code befüllt wird
        private bool capturing, hotkeyRegistered, closing;

        // Laufzustand
        private bool running, paused, holdActive, lastPressOk;
        private Step[] runSteps = new Step[0];
        private bool runScanCode, runHoldMode;
        private string runTarget = string.Empty;
        private double runIntervalMs;
        private int runJitter, stepIndex;
        private long runRepeat, presses, cycles;
        private Phase phase;
        private DateTime nextDueUtc, releaseUtc, gapUntilUtc, notBeforeUtc, holdSinceUtc, lastProcessCheckUtc;
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
            RefreshProfileList(settings.Profile);

            ResumeLayout(false);
            PerformLayout();

            timer.Tick += OnTick;
            SetStatus("Bereit. Ablauf zusammenstellen und auf Start klicken.");
        }

        // ------------------------------------------------------------------ Aufbau der Oberfläche
        // Feste Positionen (wie vom Visual-Studio-Designer erzeugt) bezogen auf 96 DPI;
        // bei höherer Bildschirmskalierung skaliert Windows Forms alles automatisch (AutoScaleMode.Dpi).

        private const int FieldX = 150;   // Spalte der Eingabefelder innerhalb einer Gruppe
        private const int RowHeight = 30;

        private void BuildUi()
        {
            const int colWidth = 440;
            const int leftX = 10;
            const int rightX = leftX + colWidth + 10;
            const int formWidth = rightX + colWidth + 10;
            const int top = 44;

            // --- Profile
            var lblProfile = new Label { Text = "Profil:", AutoSize = true, Location = new Point(12, 14) };
            cmbProfile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(60, 10), Width = 220 };
            cmbProfile.SelectedIndexChanged += (s, e) => OnProfileSelected();
            btnProfileSave = new Button { Text = "Speichern unter …", Location = new Point(288, 9), Size = new Size(130, 25) };
            btnProfileSave.Click += (s, e) => SaveProfile();
            btnProfileDelete = new Button { Text = "Löschen", Location = new Point(424, 9), Size = new Size(80, 25) };
            btnProfileDelete.Click += (s, e) => DeleteProfile();
            var lblProfileHint = new Label
            {
                Text = "Ein Profil pro Spiel/Aufgabe: speichert Ablauf, Zeitplan und Zielfenster.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(514, 14),
            };

            // --- Ablauf (linke Spalte)
            grpSteps = NewGroup("Ablauf – die Schritte werden nacheinander ausgeführt", leftX, top, colWidth, 280);
            lstSteps = new ListBox { Location = new Point(12, 24), Size = new Size(300, 124), IntegralHeight = false };
            lstSteps.SelectedIndexChanged += (s, e) => { if (!updatingUi) LoadStepIntoEditor(); };
            btnStepAdd = NewButton("+  Hinzufügen", 108, 320, 23, (s, e) => AddStep());
            btnStepRemove = NewButton("–  Entfernen", 108, 320, 53, (s, e) => RemoveStep());
            btnStepUp = NewButton("▲  Nach oben", 108, 320, 83, (s, e) => MoveStep(-1));
            btnStepDown = NewButton("▼  Nach unten", 108, 320, 113, (s, e) => MoveStep(1));
            grpSteps.Controls.AddRange(new Control[] { lstSteps, btnStepAdd, btnStepRemove, btnStepUp, btnStepDown });

            cmbKey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, MaxDropDownItems = 20 };
            foreach (Keys k in KeyNames.CommonKeys) cmbKey.Items.Add(new KeyItem(k));
            btnCapture = new Button { Text = "Aufnehmen …", Size = new Size(100, 25) };
            btnCapture.Click += (s, e) => SetCapturing(!capturing);
            chkCtrl = NewCheck("Strg");
            chkAlt = NewCheck("Alt");
            chkShift = NewCheck("Shift");
            numHold = NewNumber(1, Step.MaxHoldMs, 80);
            numAfter = NewNumber(0, Step.MaxDelayAfterMs, 80);

            int y = 158;
            AddLabel(grpSteps, "Taste / Maustaste:", y);
            AddField(grpSteps, cmbKey, FieldX, y);
            AddField(grpSteps, btnCapture, FieldX + 178, y);
            y += RowHeight;
            AddLabel(grpSteps, "Mit gedrückt:", y);
            AddField(grpSteps, chkCtrl, FieldX, y);
            AddField(grpSteps, chkAlt, FieldX + 60, y);
            AddField(grpSteps, chkShift, FieldX + 115, y);
            y += RowHeight;
            AddLabel(grpSteps, "Haltedauer:", y);
            AddField(grpSteps, numHold, FieldX, y);
            AddNote(grpSteps, "ms", FieldX + 86, y);
            y += RowHeight;
            AddLabel(grpSteps, "Danach warten:", y);
            AddField(grpSteps, numAfter, FieldX, y);
            AddNote(grpSteps, "ms bis zum nächsten Schritt", FieldX + 86, y);

            cmbKey.SelectedIndexChanged += (s, e) => OnEditorChanged();
            chkCtrl.CheckedChanged += (s, e) => OnEditorChanged();
            chkAlt.CheckedChanged += (s, e) => OnEditorChanged();
            chkShift.CheckedChanged += (s, e) => OnEditorChanged();
            numHold.ValueChanged += (s, e) => OnEditorChanged();
            numAfter.ValueChanged += (s, e) => OnEditorChanged();

            // --- Spiel / Zielfenster (linke Spalte)
            grpTarget = NewGroup("Spiel / Zielfenster", leftX, grpSteps.Bottom + 8, colWidth, 122);
            cmbWindow = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 244, MaxDropDownItems = 20 };
            cmbWindow.Items.Add(WindowItem.Any);
            cmbWindow.DropDown += (s, e) => RefreshWindowList();
            btnRefreshWindows = new Button { Text = "↻", Size = new Size(30, 25) };
            btnRefreshWindows.Click += (s, e) => RefreshWindowList();
            new ToolTip().SetToolTip(btnRefreshWindows, "Liste der offenen Programme aktualisieren");
            AddLabel(grpTarget, "Nur senden an:", 24);
            AddField(grpTarget, cmbWindow, FieldX, 24);
            AddField(grpTarget, btnRefreshWindows, FieldX + 248, 24);
            grpTarget.Controls.Add(new Label
            {
                Text = "Pausiert automatisch, sobald ein anderes Fenster aktiv ist (z. B. nach Alt+Tab), "
                       + "und stoppt, wenn das Programm beendet wird.",
                Location = new Point(12, 56),
                Size = new Size(416, 34),
                ForeColor = SystemColors.GrayText,
            });
            chkScanCode = NewCheck("Scancode senden (für Spiele empfohlen)");
            chkScanCode.Location = new Point(12, 94);
            grpTarget.Controls.Add(chkScanCode);

            // --- Wann? (rechte Spalte)
            grpSchedule = NewGroup("Wann?", rightX, top, colWidth, 24 + 7 * RowHeight + 4);
            cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270 };
            cmbMode.Items.AddRange(new object[]
            {
                "Ablauf wiederholt ausführen",
                "Tasten dauerhaft gedrückt halten (z. B. Laufen)",
            });
            cmbMode.SelectedIndexChanged += (s, e) => UpdateModeUi();
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

            y = 24;
            AddLabel(grpSchedule, "Modus:", y);
            AddField(grpSchedule, cmbMode, FieldX, y);
            y += RowHeight;
            AddLabel(grpSchedule, "Wiederholen alle:", y);
            AddField(grpSchedule, numInterval, FieldX, y);
            AddField(grpSchedule, cmbUnit, FieldX + 76, y);
            y += RowHeight;
            AddLabel(grpSchedule, "Zufällig variieren:", y);
            AddNote(grpSchedule, "±", FieldX, y);
            AddField(grpSchedule, numJitter, FieldX + 16, y);
            AddNote(grpSchedule, "%", FieldX + 77, y);
            y += RowHeight;
            AddLabel(grpSchedule, "Wiederholungen:", y);
            AddField(grpSchedule, numRepeat, FieldX, y);
            AddNote(grpSchedule, "(0 = unbegrenzt)", FieldX + 96, y);
            y += RowHeight;
            AddLabel(grpSchedule, "Beginnen:", y);
            AddField(grpSchedule, rbDelay, FieldX, y);
            AddField(grpSchedule, numDelay, FieldX + 55, y);
            AddNote(grpSchedule, "Sekunden nach dem Start", FieldX + 121, y);
            y += RowHeight;
            AddField(grpSchedule, rbTime, FieldX, y);
            AddField(grpSchedule, dtpStart, FieldX + 55, y);
            AddNote(grpSchedule, "Uhr", FieldX + 146, y);
            y += RowHeight;
            AddLabel(grpSchedule, "Automatisch stoppen:", y);
            AddField(grpSchedule, chkStop, FieldX, y);
            AddField(grpSchedule, dtpStop, FieldX + 55, y);
            AddNote(grpSchedule, "Uhr", FieldX + 146, y);

            rbDelay.CheckedChanged += (s, e) => { if (rbDelay.Checked) rbTime.Checked = false; };
            rbTime.CheckedChanged += (s, e) => { if (rbTime.Checked) rbDelay.Checked = false; };
            numDelay.ValueChanged += (s, e) => rbDelay.Checked = true;
            dtpStart.ValueChanged += (s, e) => rbTime.Checked = true;
            dtpStop.ValueChanged += (s, e) => chkStop.Checked = true;

            // --- Optionen (rechte Spalte)
            grpOptions = NewGroup("Optionen", rightX, grpSchedule.Bottom + 8, colWidth, 24 + RowHeight + 5 * 24 + 6);
            cmbHotkey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
            foreach (Keys k in KeyNames.HotkeyKeys) cmbHotkey.Items.Add(new KeyItem(k));
            cmbHotkey.SelectedIndexChanged += (s, e) => RegisterHotkey();
            AddLabel(grpOptions, "Start/Stopp-Taste:", 24);
            AddField(grpOptions, cmbHotkey, FieldX, 24);
            AddNote(grpOptions, "(funktioniert auch im Spiel)", FieldX + 96, 24);

            chkTopMost = NewCheck("Fenster immer im Vordergrund");
            chkTopMost.CheckedChanged += (s, e) => TopMost = chkTopMost.Checked;
            chkTray = NewCheck("Beim Minimieren in den Infobereich (neben der Uhr)");
            chkHideOnStart = NewCheck("Nach dem Start unsichtbar im Hintergrund weiterlaufen");
            chkNotify = NewCheck("Benachrichtigungen anzeigen (z. B. „Fertig“)");
            chkPreventSleep = NewCheck("Standby verhindern, solange AutoKey läuft");
            int optY = 24 + RowHeight;
            foreach (CheckBox c in new[] { chkTopMost, chkTray, chkHideOnStart, chkNotify, chkPreventSleep })
            {
                c.Location = new Point(12, optY);
                grpOptions.Controls.Add(c);
                optY += 24;
            }

            // Unterkanten beider Spalten angleichen
            int bottom = Math.Max(grpTarget.Bottom, grpOptions.Bottom);
            grpTarget.Height = bottom - grpTarget.Top;
            grpOptions.Height = bottom - grpOptions.Top;

            // --- Start / Stopp
            y = bottom + 10;
            btnStart = new Button { Bounds = new Rectangle(leftX, y, colWidth, 40) };
            btnStart.Font = new Font(Font, FontStyle.Bold);
            btnStart.Click += (s, e) => StartRun();
            btnStop = new Button { Bounds = new Rectangle(rightX, y, colWidth, 40), Enabled = false };
            btnStop.Click += (s, e) => StopRun("Gestoppt.");
            y = btnStart.Bottom + 8;

            // --- Statusanzeige
            lblStatus = new Label
            {
                Bounds = new Rectangle(leftX, y, formWidth - 2 * leftX, 40),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0),
            };
            y = lblStatus.Bottom + 10;

            Controls.AddRange(new Control[]
            {
                lblProfile, cmbProfile, btnProfileSave, btnProfileDelete, lblProfileHint,
                grpSteps, grpTarget, grpSchedule, grpOptions, btnStart, btnStop, lblStatus,
            });
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

            trayIdleIcon = new Icon(appIcon, SystemInformation.SmallIconSize);
            trayRunningIcon = CreateRunningIcon(trayIdleIcon);
            tray = new NotifyIcon
            {
                Icon = trayIdleIcon,
                Text = "AutoKey",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.DoubleClick += (s, e) => RestoreFromTray();
        }

        /// <summary>Symbol für den Infobereich mit grünem Punkt = „läuft“.</summary>
        private static Icon CreateRunningIcon(Icon baseIcon)
        {
            try
            {
                using (Bitmap bitmap = baseIcon.ToBitmap())
                using (Graphics g = Graphics.FromImage(bitmap))
                using (var brush = new SolidBrush(Color.FromArgb(34, 197, 94)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    int d = Math.Max(6, bitmap.Width / 2);
                    var dot = new Rectangle(bitmap.Width - d, bitmap.Height - d, d - 1, d - 1);
                    g.FillEllipse(brush, dot);
                    g.DrawEllipse(Pens.White, dot);

                    IntPtr handle = bitmap.GetHicon();
                    try
                    {
                        using (Icon temp = Icon.FromHandle(handle))
                        {
                            return (Icon)temp.Clone();
                        }
                    }
                    finally
                    {
                        NativeMethods.DestroyIcon(handle);
                    }
                }
            }
            catch (Exception)
            {
                return baseIcon;
            }
        }

        private static GroupBox NewGroup(string title, int x, int y, int width, int height) =>
            new GroupBox { Text = title, Bounds = new Rectangle(x, y, width, height) };

        /// <summary>Beschriftung in der linken Spalte, vertikal an den Eingabefeldern ausgerichtet.</summary>
        private static void AddLabel(Control parent, string text, int y) =>
            parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(12, y + 4) });

        /// <summary>Kurzer Hinweistext hinter einem Eingabefeld.</summary>
        private static void AddNote(Control parent, string text, int x, int y) =>
            parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y + 4) });

        private static void AddField(Control parent, Control field, int x, int y)
        {
            int offset = field is CheckBox || field is RadioButton ? 2 : field is Button ? -1 : 0;
            field.Location = new Point(x, y + offset);
            parent.Controls.Add(field);
        }

        private static Button NewButton(string text, int width, int x, int y, EventHandler click)
        {
            var button = new Button { Text = text, Bounds = new Rectangle(x, y, width, 25), TextAlign = ContentAlignment.MiddleLeft };
            button.Click += click;
            return button;
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
            bool wasUpdating = updatingUi;
            updatingUi = true;
            try
            {
                ApplyTaskToUi();

                int hotkeyIndex = 0;
                for (int i = 0; i < cmbHotkey.Items.Count; i++)
                {
                    if (((KeyItem)cmbHotkey.Items[i]).Key == settings.Hotkey) hotkeyIndex = i;
                }

                cmbHotkey.SelectedIndex = hotkeyIndex;
                chkTopMost.Checked = settings.TopMost;
                chkTray.Checked = settings.MinimizeToTray;
                chkHideOnStart.Checked = settings.HideOnStart;
                chkNotify.Checked = settings.ShowNotifications;
                chkPreventSleep.Checked = settings.PreventSleep;
            }
            finally
            {
                updatingUi = wasUpdating;
            }

            UpdateButtonTexts();
        }

        /// <summary>Ablauf, Zeitplan und Zielfenster in die Oberfläche übernehmen (auch beim Laden eines Profils).</summary>
        private void ApplyTaskToUi()
        {
            bool wasUpdating = updatingUi;
            updatingUi = true;
            try
            {
                steps.Clear();
                steps.AddRange(settings.Steps.Select(s => s.Clone()));
                if (steps.Count == 0) steps.Add(new Step());
                RefreshStepList(0);
                LoadStepIntoEditor();

                chkScanCode.Checked = settings.ScanCode;
                RefreshWindowList(settings.TargetProcess);
                cmbMode.SelectedIndex = settings.HoldMode ? 1 : 0;

                SetNumber(numInterval, settings.Interval);
                cmbUnit.SelectedIndex = (int)settings.Unit;
                SetNumber(numJitter, settings.JitterPercent);
                SetNumber(numRepeat, settings.Repeat);
                SetNumber(numDelay, settings.StartDelaySeconds);
                dtpStart.Value = DateTime.Today + settings.StartTime;
                dtpStop.Value = DateTime.Today + settings.StopTime;
                if (settings.StartAtTime) rbTime.Checked = true; else rbDelay.Checked = true;
                chkStop.Checked = settings.StopAtTime;
            }
            finally
            {
                updatingUi = wasUpdating;
            }

            UpdateModeUi();
        }

        private void ReadSettingsFromUi()
        {
            settings.Steps = steps.Select(s => s.Clone()).ToList();
            settings.ScanCode = chkScanCode.Checked;
            settings.TargetProcess = SelectedTarget;
            settings.HoldMode = HoldModeSelected;

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
            settings.HideOnStart = chkHideOnStart.Checked;
            settings.ShowNotifications = chkNotify.Checked;
            settings.PreventSleep = chkPreventSleep.Checked;
            settings.Profile = SelectedProfile ?? string.Empty;
        }

        private static TimeSpan TruncateToSeconds(TimeSpan t) => new TimeSpan(t.Hours, t.Minutes, t.Seconds);

        private static void SetNumber(NumericUpDown control, decimal value) =>
            control.Value = Math.Max(control.Minimum, Math.Min(control.Maximum, value));

        private Keys SelectedKey => (cmbKey.SelectedItem as KeyItem)?.Key ?? Keys.Space;

        private Keys SelectedHotkey => (cmbHotkey.SelectedItem as KeyItem)?.Key ?? Keys.F6;

        private string SelectedTarget => (cmbWindow.SelectedItem as WindowItem)?.ProcessName ?? string.Empty;

        private bool HoldModeSelected => cmbMode.SelectedIndex == 1;

        private void UpdateModeUi()
        {
            bool repeat = !HoldModeSelected;
            numInterval.Enabled = repeat;
            cmbUnit.Enabled = repeat;
            numJitter.Enabled = repeat;
            numRepeat.Enabled = repeat;
            numHold.Enabled = repeat;
            numAfter.Enabled = repeat;
        }

        private void UpdateButtonTexts()
        {
            string hotkey = hotkeyRegistered ? "  (" + KeyNames.GetName(SelectedHotkey) + ")" : string.Empty;
            btnStart.Text = "▶  Start" + hotkey;
            btnStop.Text = "■  Stopp" + hotkey;
        }

        // ------------------------------------------------------------------ Ablauf bearbeiten

        private int SelectedStepIndex =>
            lstSteps.SelectedIndex >= 0 && lstSteps.SelectedIndex < steps.Count ? lstSteps.SelectedIndex : -1;

        private void RefreshStepList(int select)
        {
            bool wasUpdating = updatingUi;
            updatingUi = true;
            try
            {
                lstSteps.BeginUpdate();
                lstSteps.Items.Clear();
                for (int i = 0; i < steps.Count; i++) lstSteps.Items.Add(StepText(i));
                lstSteps.EndUpdate();
                lstSteps.SelectedIndex = Math.Max(0, Math.Min(select, steps.Count - 1));
            }
            finally
            {
                updatingUi = wasUpdating;
            }

            UpdateStepButtons();
        }

        private string StepText(int index) => (index + 1).ToString(CultureInfo.CurrentCulture) + ".  " + steps[index].Describe();

        private void UpdateStepButtons()
        {
            int i = SelectedStepIndex;
            btnStepRemove.Enabled = steps.Count > 1;
            btnStepUp.Enabled = i > 0;
            btnStepDown.Enabled = i >= 0 && i < steps.Count - 1;
        }

        /// <summary>Den ausgewählten Schritt in die Eingabefelder darunter laden.</summary>
        private void LoadStepIntoEditor()
        {
            int i = SelectedStepIndex;
            if (i < 0) return;

            Step step = steps[i];
            bool wasUpdating = updatingUi;
            updatingUi = true;
            try
            {
                SelectKey(step.Key);
                chkCtrl.Checked = step.Ctrl;
                chkAlt.Checked = step.Alt;
                chkShift.Checked = step.Shift;
                SetNumber(numHold, step.HoldMs);
                SetNumber(numAfter, step.DelayAfterMs);
            }
            finally
            {
                updatingUi = wasUpdating;
            }

            UpdateStepButtons();
        }

        /// <summary>Änderungen in den Eingabefeldern sofort in den ausgewählten Schritt übernehmen.</summary>
        private void OnEditorChanged()
        {
            if (updatingUi) return;
            int i = SelectedStepIndex;
            if (i < 0) return;

            Step step = steps[i];
            step.Key = SelectedKey;
            step.Ctrl = chkCtrl.Checked;
            step.Alt = chkAlt.Checked;
            step.Shift = chkShift.Checked;
            step.HoldMs = (int)numHold.Value;
            step.DelayAfterMs = (int)numAfter.Value;

            updatingUi = true;
            try
            {
                lstSteps.Items[i] = StepText(i);
            }
            finally
            {
                updatingUi = false;
            }
        }

        private void AddStep()
        {
            int i = SelectedStepIndex;
            Step step = i >= 0 ? steps[i].Clone() : new Step();
            steps.Insert(i + 1, step);
            RefreshStepList(i + 1);
            LoadStepIntoEditor();
            cmbKey.Focus();
        }

        private void RemoveStep()
        {
            int i = SelectedStepIndex;
            if (i < 0 || steps.Count <= 1) return;
            steps.RemoveAt(i);
            RefreshStepList(Math.Min(i, steps.Count - 1));
            LoadStepIntoEditor();
        }

        private void MoveStep(int delta)
        {
            int i = SelectedStepIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= steps.Count) return;

            Step tmp = steps[i];
            steps[i] = steps[j];
            steps[j] = tmp;
            RefreshStepList(j);
            LoadStepIntoEditor();
        }

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

        // ------------------------------------------------------------------ Zielfenster

        private void RefreshWindowList() => RefreshWindowList(SelectedTarget);

        private void RefreshWindowList(string select)
        {
            bool wasUpdating = updatingUi;
            updatingUi = true;
            try
            {
                cmbWindow.BeginUpdate();
                cmbWindow.Items.Clear();
                cmbWindow.Items.Add(WindowItem.Any);
                foreach (WindowItem item in WindowList.GetWindows()) cmbWindow.Items.Add(item);
                cmbWindow.EndUpdate();

                if (string.IsNullOrEmpty(select))
                {
                    cmbWindow.SelectedIndex = 0;
                    return;
                }

                foreach (WindowItem item in cmbWindow.Items)
                {
                    if (string.Equals(item.ProcessName, select, StringComparison.OrdinalIgnoreCase))
                    {
                        cmbWindow.SelectedItem = item;
                        return;
                    }
                }

                // Gespeichertes Programm läuft gerade nicht – trotzdem auswählbar lassen.
                var missing = new WindowItem(select, string.Empty, false);
                cmbWindow.Items.Add(missing);
                cmbWindow.SelectedItem = missing;
            }
            finally
            {
                updatingUi = wasUpdating;
            }
        }

        // ------------------------------------------------------------------ Profile

        private string SelectedProfile => cmbProfile.SelectedIndex > 0 ? (string)cmbProfile.SelectedItem : null;

        private void RefreshProfileList(string select)
        {
            bool wasUpdating = updatingUi;
            updatingUi = true;
            try
            {
                cmbProfile.Items.Clear();
                cmbProfile.Items.Add(NoProfile);
                foreach (string name in Settings.ListProfiles()) cmbProfile.Items.Add(name);

                int index = 0;
                if (!string.IsNullOrEmpty(select))
                {
                    for (int i = 1; i < cmbProfile.Items.Count; i++)
                    {
                        if (string.Equals((string)cmbProfile.Items[i], select, StringComparison.OrdinalIgnoreCase)) index = i;
                    }
                }

                cmbProfile.SelectedIndex = index;
            }
            finally
            {
                updatingUi = wasUpdating;
            }

            btnProfileDelete.Enabled = cmbProfile.SelectedIndex > 0;
        }

        private void OnProfileSelected()
        {
            btnProfileDelete.Enabled = cmbProfile.SelectedIndex > 0;
            if (updatingUi) return;

            string name = SelectedProfile;
            settings.Profile = name ?? string.Empty;
            if (name == null) return;

            settings.CopyTaskFrom(Settings.LoadProfile(name));
            ApplyTaskToUi();
            SetStatus("Profil „" + name + "“ geladen.");
        }

        private void SaveProfile()
        {
            OnEditorChanged();
            ReadSettingsFromUi();

            string name = PromptDialog.Show(this, "Profil speichern",
                "Name des Profils (z. B. Spiel oder Aufgabe):", SelectedProfile ?? string.Empty);
            if (name == null) return;

            name = Settings.CleanProfileName(name);
            if (name.Length == 0)
            {
                Warn("Bitte einen gültigen Namen eingeben.");
                return;
            }

            if (Settings.ProfileExists(name)
                && !string.Equals(name, SelectedProfile, StringComparison.OrdinalIgnoreCase)
                && MessageBox.Show(this, "Das Profil „" + name + "“ gibt es schon. Überschreiben?", "AutoKey",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            if (!settings.SaveProfile(name))
            {
                Warn("Das Profil konnte nicht gespeichert werden.");
                return;
            }

            settings.Profile = name;
            RefreshProfileList(name);
            SetStatus("Profil „" + name + "“ gespeichert.");
        }

        private void DeleteProfile()
        {
            string name = SelectedProfile;
            if (name == null) return;
            if (MessageBox.Show(this, "Profil „" + name + "“ wirklich löschen?", "AutoKey",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            Settings.DeleteProfile(name);
            settings.Profile = string.Empty;
            RefreshProfileList(null);
            SetStatus("Profil „" + name + "“ gelöscht.");
        }

        // ------------------------------------------------------------------ Taste aufnehmen

        private void SetCapturing(bool on)
        {
            capturing = on;
            btnCapture.Text = on ? "Taste drücken …" : "Aufnehmen …";
            SetStatus(on
                ? "Jetzt die gewünschte Taste drücken (auch mit Strg/Alt/Shift). Maustasten bitte aus der Liste wählen."
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
                    bool wasUpdating = updatingUi;
                    updatingUi = true;
                    try
                    {
                        SelectKey(code);
                        chkCtrl.Checked = (keyData & Keys.Control) != 0;
                        chkAlt.Checked = (keyData & Keys.Alt) != 0;
                        chkShift.Checked = (keyData & Keys.Shift) != 0;
                    }
                    finally
                    {
                        updatingUi = wasUpdating;
                    }

                    OnEditorChanged();
                    SetCapturing(false);
                    SetStatus("Taste übernommen: " + KeyNames.Describe(code, chkCtrl.Checked, chkAlt.Checked, chkShift.Checked));
                }

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
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

            OnEditorChanged(); // gerade getippte Werte übernehmen
            ReadSettingsFromUi();
            double intervalMs = settings.IntervalMilliseconds;

            Step conflict = settings.Steps.FirstOrDefault(s => s.Key == settings.Hotkey && !s.Ctrl && !s.Alt && !s.Shift);
            if (conflict != null)
            {
                Warn("Die Taste " + KeyNames.GetName(conflict.Key) + " ist gleichzeitig die Start/Stopp-Taste.\n\n"
                     + "Bitte unter „Optionen“ eine andere Start/Stopp-Taste wählen.");
                return;
            }

            if (!settings.HoldMode && intervalMs < MinIntervalMs)
            {
                Warn("Das Intervall muss mindestens " + MinIntervalMs + " Millisekunden betragen.");
                return;
            }

            if (settings.TargetProcess.Length > 0 && !WindowList.IsRunning(settings.TargetProcess))
            {
                Warn("„" + settings.TargetProcess + ".exe“ läuft gerade nicht.\n\n"
                     + "Bitte zuerst das Spiel bzw. Programm starten – oder unter „Nur senden an“ "
                     + "„(jedes aktive Fenster)“ wählen.");
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

            DateTime now = DateTime.UtcNow;
            runSteps = settings.Steps.Select(s => s.Clone()).ToArray();
            runScanCode = settings.ScanCode;
            runHoldMode = settings.HoldMode;
            runTarget = settings.TargetProcess;
            runIntervalMs = intervalMs;
            runJitter = settings.JitterPercent;
            runRepeat = settings.Repeat;
            presses = 0;
            cycles = 0;
            stepIndex = 0;
            phase = Phase.Waiting;
            paused = false;
            holdActive = false;
            sendWarning = null;
            nextDueUtc = firstLocal.ToUniversalTime();
            notBeforeUtc = DateTime.MinValue;
            holdSinceUtc = DateTime.MinValue;
            lastProcessCheckUtc = now;
            running = true;

            settings.Save();
            if (settings.PreventSleep) NativeMethods.PreventSleep(true);
            SetRunningUi(true);
            timer.Start();
            UpdateRunStatus(now);

            if (settings.HideOnStart && Visible)
            {
                Hide();
                Notify("AutoKey läuft im Hintergrund. Stoppen mit " + KeyNames.GetName(settings.Hotkey)
                       + " oder über das Symbol neben der Uhr.");
            }
        }

        private void StopRun(string message)
        {
            if (!running) return;

            timer.Stop();
            ReleaseEverything();
            running = false;
            paused = false;
            NativeMethods.PreventSleep(false);
            SetRunningUi(false);

            string text = message;
            if (runHoldMode)
            {
                if (holdSinceUtc != DateTime.MinValue) text += " Gehalten: " + FormatDuration(DateTime.UtcNow - holdSinceUtc) + ".";
            }
            else
            {
                if (runSteps.Length > 1) text += " Durchläufe: " + Count(cycles) + ",";
                text += " Ausgeführte Tastendrücke: " + Count(presses) + ".";
            }

            SetStatus(text);
            if (!Visible && !closing) Notify(text);
        }

        private void SetRunningUi(bool isRunning)
        {
            grpSteps.Enabled = !isRunning;
            grpTarget.Enabled = !isRunning;
            grpSchedule.Enabled = !isRunning;
            cmbProfile.Enabled = !isRunning;
            btnProfileSave.Enabled = !isRunning;
            btnProfileDelete.Enabled = !isRunning && cmbProfile.SelectedIndex > 0;
            cmbHotkey.Enabled = !isRunning;
            btnStart.Enabled = !isRunning;
            btnStop.Enabled = isRunning;
            trayToggle.Text = isRunning ? "Stopp" : "Start";
            tray.Icon = isRunning ? trayRunningIcon : trayIdleIcon;
            Text = isRunning ? "AutoKey – läuft" : "AutoKey " + AppVersion;
            SetTrayText(isRunning ? "AutoKey – läuft" : "AutoKey – gestoppt");
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!running) return;
            DateTime now = DateTime.UtcNow;

            // Nur senden, wenn das gewählte Programm im Vordergrund ist.
            if (runTarget.Length > 0)
            {
                if ((now - lastProcessCheckUtc).TotalMilliseconds >= 1000)
                {
                    lastProcessCheckUtc = now;
                    if (!WindowList.IsRunning(runTarget))
                    {
                        StopRun(runTarget + ".exe wurde beendet.");
                        return;
                    }
                }

                bool active = string.Equals(WindowList.ForegroundProcessName(), runTarget, StringComparison.OrdinalIgnoreCase);
                if (!active)
                {
                    if (!paused) PauseRun();
                    UpdateRunStatus(now);
                    return;
                }

                if (paused) ResumeRun(now);
            }

            if (phase != Phase.Holding && stopUtc.HasValue && now >= stopUtc.Value)
            {
                StopRun("Stoppzeit erreicht.");
                return;
            }

            if (runHoldMode) TickHoldMode(now);
            else TickSequence(now);

            if (running) UpdateRunStatus(now);
        }

        /// <summary>Modus „dauerhaft gedrückt halten“: alle Tasten des Ablaufs gleichzeitig halten.</summary>
        private void TickHoldMode(DateTime now)
        {
            if (holdActive || now < nextDueUtc || now < notBeforeUtc) return;

            bool ok = true;
            foreach (Step step in runSteps) ok &= SendDown(step);
            ReportSendResult(ok);
            holdActive = true;
            if (holdSinceUtc == DateTime.MinValue) holdSinceUtc = now;
        }

        /// <summary>Modus „Ablauf wiederholt ausführen“: Schritt für Schritt drücken, halten, warten.</summary>
        private void TickSequence(DateTime now)
        {
            switch (phase)
            {
                case Phase.Waiting:
                    if (now >= nextDueUtc && now >= notBeforeUtc)
                    {
                        stepIndex = 0;
                        nextDueUtc = ComputeNextDue(nextDueUtc, now);
                        StartStep(now);
                    }

                    break;

                case Phase.Holding:
                    if (now < releaseUtc) break;

                    Step step = runSteps[stepIndex];
                    SendUp(step);
                    if (lastPressOk) presses++; // blockierte Eingaben nicht mitzählen
                    stepIndex++;

                    if (stepIndex < runSteps.Length)
                    {
                        phase = Phase.Gap;
                        gapUntilUtc = now.AddMilliseconds(step.DelayAfterMs);
                        break;
                    }

                    // Durchlauf beendet
                    cycles++;
                    SetTrayText("AutoKey – läuft (" + Count(runSteps.Length > 1 ? cycles : presses) + "×)");
                    if (runRepeat > 0 && cycles >= runRepeat)
                    {
                        StopRun("Fertig.");
                        return;
                    }

                    phase = Phase.Waiting;
                    notBeforeUtc = now.AddMilliseconds(step.DelayAfterMs);
                    break;

                case Phase.Gap:
                    if (now >= gapUntilUtc) StartStep(now);
                    break;
            }
        }

        private void StartStep(DateTime now)
        {
            Step step = runSteps[stepIndex];
            ReportSendResult(SendDown(step));
            phase = Phase.Holding;
            releaseUtc = now.AddMilliseconds(step.HoldMs);
        }

        /// <summary>Zielfenster nicht mehr im Vordergrund: alles loslassen und warten.</summary>
        private void PauseRun()
        {
            paused = true;
            if (phase == Phase.Holding)
            {
                SendUp(runSteps[stepIndex]);
                phase = Phase.Gap;               // unterbrochenen Schritt nach dem Fortsetzen wiederholen
                gapUntilUtc = DateTime.MinValue;
            }

            if (holdActive)
            {
                ReleaseHeldKeys();
                holdActive = false;
            }

            tray.Icon = trayIdleIcon;
        }

        private void ResumeRun(DateTime now)
        {
            paused = false;
            DateTime resumeAt = now.AddMilliseconds(ResumeDelayMs);
            if (notBeforeUtc < resumeAt) notBeforeUtc = resumeAt;
            if (gapUntilUtc < resumeAt) gapUntilUtc = resumeAt;
            tray.Icon = trayRunningIcon;
        }

        private void ReleaseEverything()
        {
            if (phase == Phase.Holding && stepIndex < runSteps.Length)
            {
                SendUp(runSteps[stepIndex]);
                phase = Phase.Waiting;
            }

            if (holdActive)
            {
                ReleaseHeldKeys();
                holdActive = false;
            }
        }

        private void ReleaseHeldKeys()
        {
            for (int i = runSteps.Length - 1; i >= 0; i--) SendUp(runSteps[i]);
        }

        private DateTime ComputeNextDue(DateTime previousDue, DateTime now)
        {
            double ms = runIntervalMs;
            if (runJitter > 0) ms *= 1.0 + (random.NextDouble() * 2.0 - 1.0) * runJitter / 100.0;
            ms = Math.Max(ms, MinIntervalMs);

            // Vom geplanten Zeitpunkt aus rechnen (kein Driften), aber Verpasstes nicht nachholen.
            DateTime next = previousDue.AddMilliseconds(ms);
            return next > now ? next : now.AddMilliseconds(ms);
        }

        private bool SendDown(Step step)
        {
            try
            {
                return KeySender.Down(step.Key, step.Ctrl, step.Alt, step.Shift, runScanCode);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void SendUp(Step step)
        {
            try
            {
                KeySender.Up(step.Key, step.Ctrl, step.Alt, step.Shift, runScanCode);
            }
            catch (Exception)
            {
                // ignorieren
            }
        }

        private void ReportSendResult(bool ok)
        {
            lastPressOk = ok;
            sendWarning = ok ? null : BlockedMessage;
        }

        // ------------------------------------------------------------------ Status

        private void UpdateRunStatus(DateTime now)
        {
            if (sendWarning != null && !paused)
            {
                SetStatus(sendWarning);
                return;
            }

            string clickHint = runTarget.Length == 0 ? " Jetzt ins Zielfenster klicken!" : string.Empty;
            string text;

            if (paused)
            {
                text = "Pausiert – " + runTarget + ".exe ist nicht im Vordergrund. Es geht automatisch weiter, sobald du dorthin wechselst.";
            }
            else if (runHoldMode)
            {
                text = holdActive
                    ? "Hält gedrückt: " + string.Join(" + ", runSteps.Select(s => s.KeyText)) + " – seit " + FormatDuration(now - holdSinceUtc)
                    : "Wartet – Halten beginnt um " + LocalTime(nextDueUtc) + " (in " + FormatRemaining(nextDueUtc - now) + ")." + clickHint;
            }
            else if (cycles == 0 && phase == Phase.Waiting)
            {
                text = "Wartet – Start um " + LocalTime(nextDueUtc) + " (in " + FormatRemaining(nextDueUtc - now) + ")." + clickHint;
            }
            else
            {
                string limit = runRepeat > 0 ? " von " + Count(runRepeat) : string.Empty;
                if (runSteps.Length > 1)
                {
                    string where = phase == Phase.Waiting
                        ? "nächster Durchlauf in " + FormatRemaining(Later(nextDueUtc, notBeforeUtc) - now)
                        : "Schritt " + (stepIndex + 1) + " von " + runSteps.Length;
                    text = "Läuft – " + Count(cycles) + limit + " Durchläufe – " + where;
                }
                else
                {
                    text = "Läuft – " + Count(presses) + limit + " Tastendrücke – nächster in "
                           + FormatRemaining(Later(nextDueUtc, notBeforeUtc) - now);
                }
            }

            if (stopUtc.HasValue) text += " – Stopp um " + stopUtc.Value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
            SetStatus(text);
        }

        private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

        private static string LocalTime(DateTime utc) => utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

        private static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

        private static string FormatRemaining(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            if (span.TotalSeconds < 10) return span.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s";
            if (span.TotalSeconds < 60) return Math.Ceiling(span.TotalSeconds).ToString(CultureInfo.CurrentCulture) + " s";
            return FormatDuration(TimeSpan.FromSeconds(Math.Ceiling(span.TotalSeconds)));
        }

        private static string FormatDuration(TimeSpan span)
        {
            long seconds = Math.Max(0, (long)span.TotalSeconds);
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

        private void Notify(string text)
        {
            if (chkNotify.Checked) tray.ShowBalloonTip(4000, "AutoKey", text, ToolTipIcon.Info);
        }

        private void Warn(string message) =>
            MessageBox.Show(this, message, "AutoKey", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        private void ShowAbout()
        {
            MessageBox.Show(
                "AutoKey " + AppVersion + "\n\n"
                + "Drückt automatisch Tasten und Maustasten – einzeln oder als Ablauf, in festen Abständen, "
                + "zu einer bestimmten Uhrzeit oder dauerhaft gehalten.\n\n"
                + "Einstellungen und Profile werden gespeichert in:\n" + Settings.BaseDirectory + "\n\n"
                + "Startoptionen:\n  /start  – sofort mit den gespeicherten Einstellungen loslegen\n  /tray   – unsichtbar im Infobereich starten",
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
            OnEditorChanged();
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
