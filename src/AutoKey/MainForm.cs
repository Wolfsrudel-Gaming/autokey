using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AutoKey
{
    internal sealed class MainForm : Form
    {
        private enum Phase
        {
            Waiting,  // wartet auf den nächsten Durchlauf
            Holding,  // Taste eines Schritts ist gedrückt
            Typing,   // Text wird Zeichen für Zeichen getippt
            Gap,      // Pause zwischen zwei Schritten
        }

        private enum CaptureTarget
        {
            None,
            StepKey,
            StartHotkey,
            PauseHotkey,
        }

        private const int StartHotkeyId = 0xA0C1;
        private const int PauseHotkeyId = 0xA0C2;
        private const int MinIntervalMs = 20;
        private const int ResumeDelayMs = 300; // kurze Pause, nachdem das Zielfenster wieder aktiv geworden ist
        private const string NoProfile = "(kein Profil)";
        private const string BlockedMessage =
            "Windows hat die Eingabe blockiert. Läuft das Zielprogramm als Administrator? Dann AutoKey ebenfalls als Administrator starten.";

        private static readonly int[] ToneStart = { 880, 90, 1175, 130 };
        private static readonly int[] ToneStop = { 1175, 90, 660, 160 };
        private static readonly int[] TonePause = { 660, 160 };
        private static readonly int[] ToneResume = { 880, 160 };

        private static readonly string AppVersion =
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        private readonly Settings settings;
        private readonly bool startImmediately;
        private readonly bool startInTray;
        private readonly string startProfile;
        private readonly Random random = new Random();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 10 };
        private readonly System.Windows.Forms.Timer pickTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400 };
        private readonly Icon appIcon;
        private Icon trayIdleIcon, trayRunningIcon;

        // Oberfläche
        private ComboBox cmbProfile, cmbType, cmbKey, cmbWheelDir, cmbWindow, cmbMode, cmbUnit;
        private Button btnProfileSave, btnProfileDelete, btnStepAdd, btnStepRemove, btnStepUp, btnStepDown, btnRecord;
        private Button btnCapture, btnPickPosition, btnRefreshWindows, btnHotkey, btnPauseHotkey, btnPauseClear, btnStart, btnStop;
        private GroupBox grpSteps, grpTarget, grpSchedule, grpOptions;
        private ListBox lstSteps;
        private Label lblKey, lblText, lblWheel, lblWheelNote, lblMods, lblHold, lblHoldNote, lblPosition, lblHotkey, lblPauseHotkey;
        private TextBox txtText;
        private CheckBox chkCtrl, chkAlt, chkShift, chkMouseFixed, chkScanCode, chkStop;
        private CheckBox chkTopMost, chkTray, chkHideOnStart, chkNotify, chkBeep, chkPreventSleep;
        private NumericUpDown numHold, numAfter, numWheel, numX, numY, numInterval, numJitter, numRepeat, numDelay;
        private RadioButton rbDelay, rbTime;
        private DateTimePicker dtpStart, dtpStop;
        private Label lblStatus;
        private NotifyIcon tray;
        private ToolStripMenuItem trayToggle, trayPause;

        // Bearbeitung
        private readonly List<Step> steps = new List<Step>();
        private bool updatingUi; // unterdrückt Ereignisse, während die Oberfläche per Code befüllt wird
        private CaptureTarget capture = CaptureTarget.None;
        private bool startHotkeyOk, pauseHotkeyOk, closing;
        private int pickCountdown;
        private MacroRecorder recorder;
        private RecordForm recordForm;

        // Laufzustand
        private bool running, paused, userPaused, holdActive, lastPressOk;
        private Step[] runSteps = new Step[0];
        private bool runScanCode, runHoldMode;
        private string runTarget = string.Empty;
        private double runIntervalMs;
        private int runJitter, stepIndex, textIndex;
        private long runRepeat, presses, cycles;
        private Phase phase;
        private DateTime runStartedUtc, nextDueUtc, releaseUtc, gapUntilUtc, notBeforeUtc, nextCharUtc;
        private DateTime holdSinceUtc, lastProcessCheckUtc;
        private DateTime? stopUtc;
        private string sendWarning;
        private string lastStatus = string.Empty, lastTrayText = string.Empty, lastTitle = string.Empty;

        public MainForm(bool startImmediately, bool startInTray, string startProfile)
        {
            this.startImmediately = startImmediately;
            this.startInTray = startInTray;
            this.startProfile = startProfile;
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

            string profileHint = null;
            if (!string.IsNullOrEmpty(startProfile))
            {
                if (Settings.ProfileExists(startProfile))
                {
                    settings.Profile = startProfile;
                    settings.CopyTaskFrom(Settings.LoadProfile(startProfile));
                    ApplyTaskToUi();
                    profileHint = "Profil „" + startProfile + "“ geladen.";
                }
                else
                {
                    profileHint = "Profil „" + startProfile + "“ wurde nicht gefunden.";
                }
            }

            RefreshProfileList(settings.Profile);

            ResumeLayout(false);
            PerformLayout();

            timer.Tick += OnTick;
            pickTimer.Tick += OnPickTick;
            SetStatus(profileHint ?? "Bereit. Ablauf zusammenstellen oder aufzeichnen, dann auf Start klicken.");
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
            grpSteps = NewGroup("Ablauf – die Schritte werden nacheinander ausgeführt", leftX, top, colWidth, 365);
            lstSteps = new ListBox { Location = new Point(12, 24), Size = new Size(300, 146), IntegralHeight = false };
            lstSteps.SelectedIndexChanged += (s, e) => { if (!updatingUi) LoadStepIntoEditor(); };
            lstSteps.KeyDown += OnStepListKeyDown;
            lstSteps.DoubleClick += (s, e) => { if (cmbType.SelectedIndex == 0) SetCapture(CaptureTarget.StepKey); };
            btnStepAdd = NewButton("+  Hinzufügen", 108, 320, 20, (s, e) => AddStep());
            btnStepRemove = NewButton("–  Entfernen", 108, 320, 50, (s, e) => RemoveStep());
            btnStepUp = NewButton("▲  Nach oben", 108, 320, 80, (s, e) => MoveStep(-1));
            btnStepDown = NewButton("▼  Nach unten", 108, 320, 110, (s, e) => MoveStep(1));
            btnRecord = NewButton("●  Aufzeichnen …", 108, 320, 140, (s, e) => StartRecording());
            btnRecord.ForeColor = Color.FromArgb(180, 0, 0);
            grpSteps.Controls.AddRange(new Control[] { lstSteps, btnStepAdd, btnStepRemove, btnStepUp, btnStepDown, btnRecord });
            tips.SetToolTip(lstSteps, "Entf = Schritt entfernen, Einfg = hinzufügen, Strg+Pfeil = verschieben, Doppelklick = Taste aufnehmen");
            tips.SetToolTip(btnRecord, "Tasten, Mausklicks und Mausrad im Spiel vorführen – AutoKey macht daraus die Schritte.");

            cmbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
            cmbType.Items.AddRange(new object[] { "Taste / Maustaste", "Text eintippen", "Mausrad drehen" });
            cmbKey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, MaxDropDownItems = 20 };
            foreach (Keys k in KeyNames.CommonKeys) cmbKey.Items.Add(new KeyItem(k));
            btnCapture = new Button { Text = "Aufnehmen …", Size = new Size(100, 25) };
            btnCapture.Click += (s, e) => SetCapture(capture == CaptureTarget.StepKey ? CaptureTarget.None : CaptureTarget.StepKey);
            txtText = new TextBox { Width = 278, MaxLength = Step.MaxTextLength };
            tips.SetToolTip(txtText, "Wird Zeichen für Zeichen getippt – z. B. ein Chat-Befehl. Für Enter danach einen Schritt „Enter“ anhängen.");
            numWheel = NewNumber(1, Step.MaxWheel, 60);
            lblWheelNote = NewNote("Rasten", FieldX + 66, 0);
            cmbWheelDir = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            cmbWheelDir.Items.AddRange(new object[] { "nach oben", "nach unten" });
            chkCtrl = NewCheck("Strg");
            chkAlt = NewCheck("Alt");
            chkShift = NewCheck("Shift");
            numHold = NewNumber(1, Step.MaxHoldMs, 80);
            tips.SetToolTip(numHold, "Wie lange die Taste gedrückt bleibt. Viele Spiele brauchen mindestens 30–50 ms.");
            chkMouseFixed = NewCheck("fest:");
            tips.SetToolTip(chkMouseFixed, "Die Maus wird vor dem Klick an diese Bildschirmposition gesetzt.");
            numX = NewNumber(-30000, 30000, 58);
            numY = NewNumber(-30000, 30000, 58);
            btnPickPosition = new Button { Text = "Position (3 s)", Size = new Size(100, 25) };
            btnPickPosition.Click += (s, e) => StartPositionPick();
            tips.SetToolTip(btnPickPosition, "Nach dem Klick 3 Sekunden Zeit, die Maus an die gewünschte Stelle zu bewegen.");
            numAfter = NewNumber(0, Step.MaxDelayAfterMs, 80);

            int y = 180;
            AddLabel(grpSteps, "Art:", y);
            AddField(grpSteps, cmbType, FieldX, y);
            y += RowHeight;
            lblKey = AddLabel(grpSteps, "Taste / Maustaste:", y);
            AddField(grpSteps, cmbKey, FieldX, y);
            AddField(grpSteps, btnCapture, FieldX + 178, y);
            lblText = AddLabel(grpSteps, "Text:", y);
            AddField(grpSteps, txtText, FieldX, y);
            lblWheel = AddLabel(grpSteps, "Mausrad:", y);
            AddField(grpSteps, numWheel, FieldX, y);
            lblWheelNote.Location = new Point(FieldX + 66, y + 4);
            grpSteps.Controls.Add(lblWheelNote);
            AddField(grpSteps, cmbWheelDir, FieldX + 120, y);
            y += RowHeight;
            lblMods = AddLabel(grpSteps, "Mit gedrückt:", y);
            AddField(grpSteps, chkCtrl, FieldX, y);
            AddField(grpSteps, chkAlt, FieldX + 60, y);
            AddField(grpSteps, chkShift, FieldX + 115, y);
            y += RowHeight;
            lblHold = AddLabel(grpSteps, "Haltedauer:", y);
            AddField(grpSteps, numHold, FieldX, y);
            lblHoldNote = NewNote("ms", FieldX + 86, y);
            grpSteps.Controls.Add(lblHoldNote);
            y += RowHeight;
            lblPosition = AddLabel(grpSteps, "Mausposition:", y);
            AddField(grpSteps, chkMouseFixed, FieldX, y);
            AddField(grpSteps, numX, FieldX + 52, y);
            AddField(grpSteps, numY, FieldX + 114, y);
            AddField(grpSteps, btnPickPosition, FieldX + 178, y);
            y += RowHeight;
            AddLabel(grpSteps, "Danach warten:", y);
            AddField(grpSteps, numAfter, FieldX, y);
            AddNote(grpSteps, "ms bis zum nächsten Schritt", FieldX + 86, y);

            cmbType.SelectedIndexChanged += (s, e) => { OnEditorChanged(); UpdateEditorVisibility(); };
            cmbKey.SelectedIndexChanged += (s, e) => { OnEditorChanged(); UpdateEditorVisibility(); };
            txtText.TextChanged += (s, e) => OnEditorChanged();
            numWheel.ValueChanged += (s, e) => OnEditorChanged();
            cmbWheelDir.SelectedIndexChanged += (s, e) => OnEditorChanged();
            chkCtrl.CheckedChanged += (s, e) => OnEditorChanged();
            chkAlt.CheckedChanged += (s, e) => OnEditorChanged();
            chkShift.CheckedChanged += (s, e) => OnEditorChanged();
            numHold.ValueChanged += (s, e) => OnEditorChanged();
            chkMouseFixed.CheckedChanged += (s, e) => OnEditorChanged();
            numX.ValueChanged += (s, e) => { if (!updatingUi) chkMouseFixed.Checked = true; OnEditorChanged(); };
            numY.ValueChanged += (s, e) => { if (!updatingUi) chkMouseFixed.Checked = true; OnEditorChanged(); };
            numAfter.ValueChanged += (s, e) => OnEditorChanged();

            // --- Spiel / Zielfenster (linke Spalte)
            grpTarget = NewGroup("Spiel / Zielfenster", leftX, grpSteps.Bottom + 8, colWidth, 122);
            cmbWindow = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 244, MaxDropDownItems = 20 };
            cmbWindow.Items.Add(WindowItem.Any);
            cmbWindow.DropDown += (s, e) => RefreshWindowList();
            btnRefreshWindows = new Button { Text = "↻", Size = new Size(30, 25) };
            btnRefreshWindows.Click += (s, e) => RefreshWindowList();
            tips.SetToolTip(btnRefreshWindows, "Liste der offenen Programme aktualisieren");
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
            tips.SetToolTip(chkScanCode, "Sendet Tasten so, wie sie von der Tastatur kommen. Hilft, wenn ein Spiel die Tasten sonst ignoriert.");
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
            tips.SetToolTip(numJitter, "Verändert Abstände, Haltedauern und Wartezeiten zufällig um bis zu diesen Prozentsatz – wirkt weniger mechanisch.");
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
            AddNote(grpSchedule, "%   (auch Haltedauern und Pausen)", FieldX + 77, y);
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
            grpOptions = NewGroup("Optionen", rightX, grpSchedule.Bottom + 8, colWidth, 24 + 2 * RowHeight + 6 * 24 + 6);
            lblHotkey = NewValueLabel(110);
            btnHotkey = new Button { Text = "Ändern …", Size = new Size(80, 25) };
            btnHotkey.Click += (s, e) => SetCapture(capture == CaptureTarget.StartHotkey ? CaptureTarget.None : CaptureTarget.StartHotkey);
            tips.SetToolTip(btnHotkey, "F-Taste oder Kombination mit Strg/Alt/Shift wählen – z. B. Strg+F6, wenn das Spiel F6 selbst benutzt.");
            lblPauseHotkey = NewValueLabel(110);
            btnPauseHotkey = new Button { Text = "Ändern …", Size = new Size(80, 25) };
            btnPauseHotkey.Click += (s, e) => SetCapture(capture == CaptureTarget.PauseHotkey ? CaptureTarget.None : CaptureTarget.PauseHotkey);
            btnPauseClear = new Button { Text = "Keine", Size = new Size(60, 25) };
            btnPauseClear.Click += (s, e) => { settings.PauseHotkey = Keys.None; RegisterHotkeys(); };
            tips.SetToolTip(lblPauseHotkey, "Unterbricht den Ablauf, ohne die Zähler zurückzusetzen – nochmal drücken macht weiter.");

            y = 24;
            AddLabel(grpOptions, "Start/Stopp-Taste:", y);
            AddField(grpOptions, lblHotkey, FieldX, y);
            AddField(grpOptions, btnHotkey, FieldX + 116, y);
            y += RowHeight;
            AddLabel(grpOptions, "Pause-Taste:", y);
            AddField(grpOptions, lblPauseHotkey, FieldX, y);
            AddField(grpOptions, btnPauseHotkey, FieldX + 116, y);
            AddField(grpOptions, btnPauseClear, FieldX + 202, y);
            y += RowHeight;

            chkTopMost = NewCheck("Fenster immer im Vordergrund");
            chkTopMost.CheckedChanged += (s, e) => TopMost = chkTopMost.Checked;
            chkTray = NewCheck("Beim Minimieren in den Infobereich (neben der Uhr)");
            chkHideOnStart = NewCheck("Nach dem Start unsichtbar im Hintergrund weiterlaufen");
            chkNotify = NewCheck("Benachrichtigungen anzeigen (z. B. „Fertig“)");
            chkBeep = NewCheck("Signalton bei Start, Stopp und Pause");
            tips.SetToolTip(chkBeep, "Kurzer Ton, damit du im Spiel hörst, dass AutoKey gestartet bzw. gestoppt wurde.");
            chkPreventSleep = NewCheck("Standby verhindern, solange AutoKey läuft");
            foreach (CheckBox c in new[] { chkTopMost, chkTray, chkHideOnStart, chkNotify, chkBeep, chkPreventSleep })
            {
                c.Location = new Point(12, y);
                grpOptions.Controls.Add(c);
                y += 24;
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
            trayPause = new ToolStripMenuItem("Pause", null, (s, e) => TogglePause()) { Enabled = false };
            menu.Items.Add(trayToggle);
            menu.Items.Add(trayPause);
            menu.Items.Add("Fenster anzeigen", null, (s, e) => RestoreFromTray());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Profilordner öffnen", null, (s, e) => OpenProfileFolder());
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
        private static Label AddLabel(Control parent, string text, int y)
        {
            var label = new Label { Text = text, AutoSize = true, Location = new Point(12, y + 4) };
            parent.Controls.Add(label);
            return label;
        }

        /// <summary>Kurzer Hinweistext hinter einem Eingabefeld.</summary>
        private static Label NewNote(string text, int x, int y) =>
            new Label { Text = text, AutoSize = true, Location = new Point(x, y + 4) };

        private static void AddNote(Control parent, string text, int x, int y) =>
            parent.Controls.Add(NewNote(text, x, y));

        /// <summary>Umrahmte Anzeige eines Werts (z. B. der Hotkey).</summary>
        private static Label NewValueLabel(int width) =>
            new Label
            {
                AutoSize = false,
                Size = new Size(width, 23),
                BorderStyle = BorderStyle.Fixed3D,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = SystemColors.Window,
            };

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
                chkTopMost.Checked = settings.TopMost;
                chkTray.Checked = settings.MinimizeToTray;
                chkHideOnStart.Checked = settings.HideOnStart;
                chkNotify.Checked = settings.ShowNotifications;
                chkBeep.Checked = settings.Beep;
                chkPreventSleep.Checked = settings.PreventSleep;
            }
            finally
            {
                updatingUi = wasUpdating;
            }

            UpdateHotkeyUi();
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

            // Hotkeys stehen bereits in settings (werden beim Ändern direkt gesetzt)
            settings.TopMost = chkTopMost.Checked;
            settings.MinimizeToTray = chkTray.Checked;
            settings.HideOnStart = chkHideOnStart.Checked;
            settings.ShowNotifications = chkNotify.Checked;
            settings.Beep = chkBeep.Checked;
            settings.PreventSleep = chkPreventSleep.Checked;
            settings.Profile = SelectedProfile ?? string.Empty;
        }

        private static TimeSpan TruncateToSeconds(TimeSpan t) => new TimeSpan(t.Hours, t.Minutes, t.Seconds);

        private static void SetNumber(NumericUpDown control, decimal value) =>
            control.Value = Math.Max(control.Minimum, Math.Min(control.Maximum, value));

        private Keys SelectedKey => (cmbKey.SelectedItem as KeyItem)?.Key ?? Keys.Space;

        private string SelectedTarget => (cmbWindow.SelectedItem as WindowItem)?.ProcessName ?? string.Empty;

        private bool HoldModeSelected => cmbMode.SelectedIndex == 1;

        private void UpdateModeUi()
        {
            bool repeat = !HoldModeSelected;
            numInterval.Enabled = repeat;
            cmbUnit.Enabled = repeat;
            numRepeat.Enabled = repeat;
            numAfter.Enabled = repeat;
            UpdateEditorVisibility();
        }

        private void UpdateHotkeyUi()
        {
            lblHotkey.Text = KeyNames.DescribeCombo(settings.Hotkey);
            lblHotkey.ForeColor = startHotkeyOk || !IsHandleCreated ? SystemColors.WindowText : Color.Firebrick;
            lblPauseHotkey.Text = KeyNames.DescribeCombo(settings.PauseHotkey);
            lblPauseHotkey.ForeColor = pauseHotkeyOk || settings.PauseHotkey == Keys.None || !IsHandleCreated
                ? SystemColors.WindowText
                : Color.Firebrick;
            btnPauseClear.Enabled = settings.PauseHotkey != Keys.None;

            string hotkey = startHotkeyOk ? "  (" + KeyNames.DescribeCombo(settings.Hotkey) + ")" : string.Empty;
            btnStart.Text = "▶  Start" + hotkey;
            btnStop.Text = "■  Stopp" + hotkey;
            trayToggle.Text = (running ? "Stopp" : "Start") + hotkey;
            trayPause.Text = (userPaused ? "Weiter" : "Pause")
                             + (pauseHotkeyOk ? "  (" + KeyNames.DescribeCombo(settings.PauseHotkey) + ")" : string.Empty);
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
                cmbType.SelectedIndex = (int)step.Type;
                SelectKey(step.Key);
                txtText.Text = step.Text;
                SetNumber(numWheel, Math.Max(1, Math.Abs(step.Wheel)));
                cmbWheelDir.SelectedIndex = step.Wheel < 0 ? 1 : 0;
                chkCtrl.Checked = step.Ctrl;
                chkAlt.Checked = step.Alt;
                chkShift.Checked = step.Shift;
                SetNumber(numHold, step.HoldMs);
                chkMouseFixed.Checked = step.MouseFixed;
                SetNumber(numX, step.MouseX);
                SetNumber(numY, step.MouseY);
                SetNumber(numAfter, step.DelayAfterMs);
            }
            finally
            {
                updatingUi = wasUpdating;
            }

            UpdateEditorVisibility();
            UpdateStepButtons();
        }

        /// <summary>Je nach Art des Schritts nur die passenden Felder zeigen.</summary>
        private void UpdateEditorVisibility()
        {
            var type = (StepType)Math.Max(0, cmbType.SelectedIndex);
            bool key = type == StepType.Key;
            bool text = type == StepType.Text;
            bool wheel = type == StepType.Wheel;
            bool mouse = wheel || (key && KeySender.IsMouseButton(SelectedKey));

            lblKey.Visible = cmbKey.Visible = btnCapture.Visible = key;
            lblText.Visible = txtText.Visible = text;
            lblWheel.Visible = numWheel.Visible = lblWheelNote.Visible = cmbWheelDir.Visible = wheel;
            lblMods.Visible = chkCtrl.Visible = chkAlt.Visible = chkShift.Visible = key;
            lblHold.Visible = numHold.Visible = lblHoldNote.Visible = key || text;
            lblHold.Text = text ? "Pro Zeichen:" : "Haltedauer:";
            lblHoldNote.Text = text ? "ms Tippgeschwindigkeit" : "ms";
            numHold.Enabled = !HoldModeSelected || text;
            lblPosition.Visible = chkMouseFixed.Visible = numX.Visible = numY.Visible = btnPickPosition.Visible = mouse;
        }

        /// <summary>Änderungen in den Eingabefeldern sofort in den ausgewählten Schritt übernehmen.</summary>
        private void OnEditorChanged()
        {
            if (updatingUi) return;
            int i = SelectedStepIndex;
            if (i < 0) return;

            Step step = steps[i];
            step.Type = (StepType)Math.Max(0, cmbType.SelectedIndex);
            step.Key = SelectedKey;
            step.Text = txtText.Text;
            step.Wheel = (int)numWheel.Value * (cmbWheelDir.SelectedIndex == 1 ? -1 : 1);
            step.Ctrl = chkCtrl.Checked;
            step.Alt = chkAlt.Checked;
            step.Shift = chkShift.Checked;
            step.HoldMs = (int)numHold.Value;
            step.MouseFixed = chkMouseFixed.Checked;
            step.MouseX = (int)numX.Value;
            step.MouseY = (int)numY.Value;
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

        private void ReplaceSteps(IEnumerable<Step> newSteps, bool append)
        {
            if (!append) steps.Clear();
            steps.AddRange(newSteps);
            if (steps.Count == 0) steps.Add(new Step());
            RefreshStepList(append ? steps.Count - 1 : 0);
            LoadStepIntoEditor();
        }

        private void OnStepListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) { RemoveStep(); e.Handled = true; }
            else if (e.KeyCode == Keys.Insert) { AddStep(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Up) { MoveStep(-1); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Down) { MoveStep(1); e.Handled = true; }
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

        // ------------------------------------------------------------------ Mausposition aufnehmen

        private void StartPositionPick()
        {
            if (pickTimer.Enabled) return;
            pickCountdown = 3;
            btnPickPosition.Text = "… " + pickCountdown;
            btnPickPosition.Enabled = false;
            SetStatus("Maus jetzt an die gewünschte Stelle bewegen – Position wird in 3 Sekunden übernommen.");
            pickTimer.Start();
        }

        private void OnPickTick(object sender, EventArgs e)
        {
            pickCountdown--;
            if (pickCountdown > 0)
            {
                btnPickPosition.Text = "… " + pickCountdown;
                return;
            }

            pickTimer.Stop();
            Point p = Cursor.Position;
            bool wasUpdating = updatingUi;
            updatingUi = true;
            try
            {
                SetNumber(numX, p.X);
                SetNumber(numY, p.Y);
                chkMouseFixed.Checked = true;
            }
            finally
            {
                updatingUi = wasUpdating;
            }

            OnEditorChanged();
            btnPickPosition.Text = "Position (3 s)";
            btnPickPosition.Enabled = true;
            SetStatus("Mausposition übernommen: " + p.X + ", " + p.Y);
        }

        // ------------------------------------------------------------------ Aufzeichnung

        private void StartRecording()
        {
            if (running || recorder != null) return;
            if (capture != CaptureTarget.None) SetCapture(CaptureTarget.None);

            var ignored = new List<Keys> { settings.Hotkey & Keys.KeyCode };
            if (settings.PauseHotkey != Keys.None) ignored.Add(settings.PauseHotkey & Keys.KeyCode);
            recorder = new MacroRecorder(ignored, IsOverOwnWindow);

            if (!recorder.Start())
            {
                recorder.Dispose();
                recorder = null;
                Warn("Die Aufzeichnung konnte nicht gestartet werden.\n\n"
                     + "Windows hat das Mitlesen der Eingaben verweigert. Bitte AutoKey einmal als Administrator starten "
                     + "oder die Schritte von Hand anlegen.");
                return;
            }

            recordForm = new RecordForm(recorder, KeyNames.DescribeCombo(settings.Hotkey), appIcon);
            recordForm.FormClosed += (s, e) => FinishRecording();
            recordForm.Show(this);
            btnRecord.Enabled = false;
            SetStatus("Aufzeichnung läuft – im Spiel vorführen, Beenden mit " + KeyNames.DescribeCombo(settings.Hotkey) + " oder „Fertig“.");
        }

        private bool IsOverOwnWindow(Point p)
        {
            try
            {
                if (recordForm != null && recordForm.Visible && recordForm.Bounds.Contains(p)) return true;
                if (Visible && WindowState != FormWindowState.Minimized && Bounds.Contains(p)) return true;
            }
            catch (Exception)
            {
                // ignorieren
            }

            return false;
        }

        private void FinishRecording()
        {
            if (recorder == null) return;

            recorder.Stop();
            bool keep = recordForm != null && recordForm.DialogResult == DialogResult.OK;
            bool usePositions = recordForm == null || recordForm.UsePositions;
            List<Step> recorded = keep ? recorder.ToSteps(usePositions) : new List<Step>();

            recorder.Dispose();
            recorder = null;
            recordForm?.Dispose();
            recordForm = null;
            btnRecord.Enabled = true;
            Activate();

            if (!keep)
            {
                SetStatus("Aufzeichnung verworfen.");
                return;
            }

            if (recorded.Count == 0)
            {
                SetStatus("Es wurde nichts aufgezeichnet.");
                return;
            }

            DialogResult answer = MessageBox.Show(this,
                recorded.Count.ToString(CultureInfo.CurrentCulture) + " Schritte aufgezeichnet.\n\n"
                + "Sollen sie den bisherigen Ablauf ersetzen?\n\n"
                + "Ja = ersetzen      Nein = ans Ende anhängen",
                "AutoKey – Aufzeichnung", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;

            ReplaceSteps(recorded, answer == DialogResult.No);
            SetStatus(recorded.Count + " aufgezeichnete Schritte übernommen – Haltedauern und Wartezeiten können unten angepasst werden.");
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
            SetStatus("Profil „" + name + "“ gespeichert. Direkt damit starten: AutoKey.exe /profile:\"" + name + "\" /start");
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

        private void OpenProfileFolder()
        {
            try
            {
                Directory.CreateDirectory(Settings.ProfileDirectory);
                Process.Start("explorer.exe", "\"" + Settings.ProfileDirectory + "\"");
            }
            catch (Exception ex)
            {
                Log.Error("Profilordner öffnen", ex);
                Warn("Der Ordner konnte nicht geöffnet werden:\n" + Settings.ProfileDirectory);
            }
        }

        // ------------------------------------------------------------------ Tasten aufnehmen (Schritt und Hotkeys)

        private void SetCapture(CaptureTarget target)
        {
            capture = target;
            btnCapture.Text = target == CaptureTarget.StepKey ? "Taste drücken …" : "Aufnehmen …";
            btnHotkey.Text = target == CaptureTarget.StartHotkey ? "Drücken …" : "Ändern …";
            btnPauseHotkey.Text = target == CaptureTarget.PauseHotkey ? "Drücken …" : "Ändern …";

            switch (target)
            {
                case CaptureTarget.StepKey:
                    SetStatus("Jetzt die gewünschte Taste drücken (auch mit Strg/Alt/Shift). Maustasten bitte aus der Liste wählen.");
                    break;
                case CaptureTarget.StartHotkey:
                    SetStatus("Jetzt die neue Start/Stopp-Taste drücken – z. B. F6, Strg+F6 oder Alt+Q. Esc = abbrechen.");
                    break;
                case CaptureTarget.PauseHotkey:
                    SetStatus("Jetzt die neue Pause-Taste drücken – z. B. F7 oder Strg+F7. Esc = abbrechen.");
                    break;
                default:
                    SetStatus("Bereit.");
                    break;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (capture == CaptureTarget.None) return base.ProcessCmdKey(ref msg, keyData);

            Keys code = keyData & Keys.KeyCode;
            bool modifierOnly = code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu
                                || code == Keys.LWin || code == Keys.RWin;
            if (modifierOnly || code == Keys.None) return true;

            CaptureTarget target = capture;
            if (target == CaptureTarget.StepKey)
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
                UpdateEditorVisibility();
                SetCapture(CaptureTarget.None);
                SetStatus("Taste übernommen: " + KeyNames.DescribeCombo(keyData));
                return true;
            }

            // Hotkeys
            if (code == Keys.Escape && (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) == 0)
            {
                SetCapture(CaptureTarget.None);
                return true;
            }

            Keys combo = keyData & (Keys.KeyCode | Keys.Control | Keys.Alt | Keys.Shift);
            if (!IsHotkeyAllowed(combo))
            {
                SetStatus("Bitte eine F-Taste (F1–F24, Pause, Rollen) oder eine Kombination mit Strg/Alt/Shift wählen – "
                          + "eine einzelne Buchstaben- oder Zifferntaste würde das Tippen überall blockieren.");
                return true;
            }

            Keys other = target == CaptureTarget.StartHotkey ? settings.PauseHotkey : settings.Hotkey;
            if (combo == other)
            {
                SetStatus(KeyNames.DescribeCombo(combo) + " ist schon die andere Taste – bitte eine andere wählen.");
                return true;
            }

            if (target == CaptureTarget.StartHotkey) settings.Hotkey = combo;
            else settings.PauseHotkey = combo;
            SetCapture(CaptureTarget.None);
            RegisterHotkeys();
            settings.Save();
            return true;
        }

        private static bool IsHotkeyAllowed(Keys combo)
        {
            Keys code = combo & Keys.KeyCode;
            if ((combo & (Keys.Control | Keys.Alt | Keys.Shift)) != 0) return true;
            return (code >= Keys.F1 && code <= Keys.F24) || code == Keys.Pause || code == Keys.Scroll;
        }

        // ------------------------------------------------------------------ Globale Hotkeys

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotkeys();
            base.OnHandleDestroyed(e);
        }

        private void RegisterHotkeys()
        {
            UnregisterHotkeys();
            if (!IsHandleCreated) return;

            startHotkeyOk = RegisterHotkey(StartHotkeyId, settings.Hotkey);
            pauseHotkeyOk = settings.PauseHotkey != Keys.None && RegisterHotkey(PauseHotkeyId, settings.PauseHotkey);
            UpdateHotkeyUi();

            if (!startHotkeyOk)
            {
                SetStatus("Die Taste " + KeyNames.DescribeCombo(settings.Hotkey)
                          + " wird schon von einem anderen Programm benutzt – bitte eine andere Start/Stopp-Taste wählen.");
            }
            else if (settings.PauseHotkey != Keys.None && !pauseHotkeyOk)
            {
                SetStatus("Die Pause-Taste " + KeyNames.DescribeCombo(settings.PauseHotkey)
                          + " wird schon von einem anderen Programm benutzt – bitte eine andere wählen.");
            }
        }

        private bool RegisterHotkey(int id, Keys combo)
        {
            try
            {
                uint modifiers = NativeMethods.MOD_NOREPEAT;
                if ((combo & Keys.Control) != 0) modifiers |= NativeMethods.MOD_CONTROL;
                if ((combo & Keys.Alt) != 0) modifiers |= NativeMethods.MOD_ALT;
                if ((combo & Keys.Shift) != 0) modifiers |= NativeMethods.MOD_SHIFT;
                return NativeMethods.RegisterHotKey(Handle, id, modifiers, (uint)(combo & Keys.KeyCode));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void UnregisterHotkeys()
        {
            try
            {
                if (startHotkeyOk) NativeMethods.UnregisterHotKey(Handle, StartHotkeyId);
                if (pauseHotkeyOk) NativeMethods.UnregisterHotKey(Handle, PauseHotkeyId);
            }
            catch (Exception)
            {
                // ignorieren
            }

            startHotkeyOk = false;
            pauseHotkeyOk = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == StartHotkeyId)
                {
                    if (recordForm != null) recordForm.Finish();
                    else if (capture == CaptureTarget.None) ToggleRun();
                    return;
                }

                if (id == PauseHotkeyId)
                {
                    if (running && recordForm == null) TogglePause();
                    return;
                }
            }

            base.WndProc(ref m);
        }

        // ------------------------------------------------------------------ Start / Stopp / Pause

        private void ToggleRun()
        {
            if (running) StopRun("Gestoppt.");
            else StartRun();
        }

        private void TogglePause()
        {
            if (!running) return;
            userPaused = !userPaused;
            Tone(userPaused ? TonePause : ToneResume);
            UpdateHotkeyUi();
            if (!Visible) Notify(userPaused ? "Pausiert." : "Weiter geht's.");
        }

        private void StartRun()
        {
            if (running || recorder != null) return;
            if (capture != CaptureTarget.None) SetCapture(CaptureTarget.None);

            OnEditorChanged(); // gerade getippte Werte übernehmen
            ReadSettingsFromUi();
            double intervalMs = settings.IntervalMilliseconds;

            Keys hotkeyCode = settings.Hotkey & Keys.KeyCode;
            for (int i = 0; i < settings.Steps.Count; i++)
            {
                Step s = settings.Steps[i];
                string prefix = "Schritt " + (i + 1) + ": ";
                if (s.Type == StepType.Key && s.Key == hotkeyCode
                    && s.Ctrl == ((settings.Hotkey & Keys.Control) != 0)
                    && s.Alt == ((settings.Hotkey & Keys.Alt) != 0)
                    && s.Shift == ((settings.Hotkey & Keys.Shift) != 0))
                {
                    Warn(prefix + KeyNames.DescribeCombo(settings.Hotkey) + " ist gleichzeitig die Start/Stopp-Taste.\n\n"
                         + "Bitte unter „Optionen“ eine andere Start/Stopp-Taste wählen.");
                    return;
                }

                if (s.Type == StepType.Text && s.Text.Length == 0)
                {
                    Warn(prefix + "Es wurde kein Text eingegeben.");
                    return;
                }

                if (s.Type == StepType.Wheel && s.Wheel == 0)
                {
                    Warn(prefix + "Mausrad-Rasten dürfen nicht 0 sein.");
                    return;
                }
            }

            if (!settings.HoldMode && intervalMs < MinIntervalMs)
            {
                Warn("Das Intervall muss mindestens " + MinIntervalMs + " Millisekunden betragen.");
                return;
            }

            Step[] runList = settings.HoldMode
                ? settings.Steps.Where(s => s.Type == StepType.Key).Select(s => s.Clone()).ToArray()
                : settings.Steps.Select(s => s.Clone()).ToArray();
            if (runList.Length == 0)
            {
                Warn("Im Modus „dauerhaft gedrückt halten“ wird mindestens ein Schritt der Art „Taste / Maustaste“ benötigt.");
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
            runSteps = runList;
            runScanCode = settings.ScanCode;
            runHoldMode = settings.HoldMode;
            runTarget = settings.TargetProcess;
            runIntervalMs = intervalMs;
            runJitter = settings.JitterPercent;
            runRepeat = settings.Repeat;
            presses = 0;
            cycles = 0;
            stepIndex = 0;
            textIndex = 0;
            phase = Phase.Waiting;
            paused = false;
            userPaused = false;
            holdActive = false;
            sendWarning = null;
            runStartedUtc = now;
            nextDueUtc = firstLocal.ToUniversalTime();
            notBeforeUtc = DateTime.MinValue;
            gapUntilUtc = DateTime.MinValue;
            holdSinceUtc = DateTime.MinValue;
            lastProcessCheckUtc = now;
            running = true;

            settings.Save();
            if (settings.PreventSleep) NativeMethods.PreventSleep(true);
            NativeMethods.HighTimerResolution(true);
            SetRunningUi(true);
            timer.Start();
            Tone(ToneStart);
            UpdateRunStatus(now);

            if (settings.HideOnStart && Visible)
            {
                Hide();
                Notify("AutoKey läuft im Hintergrund. Stoppen mit " + KeyNames.DescribeCombo(settings.Hotkey)
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
            userPaused = false;
            NativeMethods.HighTimerResolution(false);
            NativeMethods.PreventSleep(false);
            SetRunningUi(false);
            if (!closing) Tone(ToneStop);

            string text = message;
            if (runHoldMode)
            {
                if (holdSinceUtc != DateTime.MinValue) text += " Gehalten: " + FormatDuration(DateTime.UtcNow - holdSinceUtc) + ".";
            }
            else
            {
                if (runSteps.Length > 1) text += " Durchläufe: " + Count(cycles) + ",";
                text += " Ausgeführte Eingaben: " + Count(presses) + ".";
            }

            text += " Laufzeit " + FormatDuration(DateTime.UtcNow - runStartedUtc) + ".";
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
            btnHotkey.Enabled = !isRunning;
            btnPauseHotkey.Enabled = !isRunning;
            btnPauseClear.Enabled = !isRunning && settings.PauseHotkey != Keys.None;
            btnStart.Enabled = !isRunning;
            btnStop.Enabled = isRunning;
            trayPause.Enabled = isRunning;
            tray.Icon = isRunning ? trayRunningIcon : trayIdleIcon;
            SetTitle(isRunning ? "AutoKey – läuft" : "AutoKey " + AppVersion);
            SetTrayText(isRunning ? "AutoKey – läuft" : "AutoKey – gestoppt");
            UpdateHotkeyUi();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!running) return;
            DateTime now = DateTime.UtcNow;

            // Nur senden, wenn das gewählte Programm im Vordergrund ist.
            bool windowInactive = false;
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

                windowInactive = !string.Equals(WindowList.ForegroundProcessName(), runTarget, StringComparison.OrdinalIgnoreCase);
            }

            if (userPaused || windowInactive)
            {
                if (!paused) PauseRun();
                UpdateRunStatus(now);
                return;
            }

            if (paused) ResumeRun(now);

            if (phase == Phase.Waiting && stopUtc.HasValue && now >= stopUtc.Value)
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
                    SendUp(runSteps[stepIndex]);
                    if (lastPressOk) presses++; // blockierte Eingaben nicht mitzählen
                    FinishStep(now);
                    break;

                case Phase.Typing:
                    if (now < nextCharUtc) break;
                    Step text = runSteps[stepIndex];
                    int consumed = KeySender.TypeCharacter(text.Text, textIndex);
                    if (consumed == 0)
                    {
                        ReportSendResult(false);
                        FinishStep(now);
                        break;
                    }

                    textIndex += consumed;
                    if (textIndex >= text.Text.Length)
                    {
                        presses++;
                        FinishStep(now);
                    }
                    else
                    {
                        nextCharUtc = now.AddMilliseconds(Jitter(text.HoldMs));
                    }

                    break;

                case Phase.Gap:
                    if (now >= gapUntilUtc) StartStep(now);
                    break;
            }
        }

        private void StartStep(DateTime now)
        {
            Step step = runSteps[stepIndex];
            if (step.UsesMousePosition) KeySender.MoveTo(step.MouseX, step.MouseY);

            switch (step.Type)
            {
                case StepType.Text:
                    textIndex = 0;
                    nextCharUtc = now;
                    phase = Phase.Typing;
                    break;

                case StepType.Wheel:
                    bool ok = KeySender.Wheel(step.Wheel);
                    ReportSendResult(ok);
                    if (ok) presses++;
                    FinishStep(now);
                    break;

                default:
                    ReportSendResult(SendDown(step));
                    phase = Phase.Holding;
                    releaseUtc = now.AddMilliseconds(Math.Max(1, Jitter(step.HoldMs)));
                    break;
            }
        }

        /// <summary>Nach einem Schritt: Pause bis zum nächsten oder Durchlauf abschließen.</summary>
        private void FinishStep(DateTime now)
        {
            Step step = runSteps[stepIndex];
            stepIndex++;

            if (stepIndex < runSteps.Length)
            {
                phase = Phase.Gap;
                gapUntilUtc = now.AddMilliseconds(Jitter(step.DelayAfterMs));
                return;
            }

            cycles++;
            SetTrayText("AutoKey – läuft (" + Count(runSteps.Length > 1 ? cycles : presses) + "×)");
            if (runRepeat > 0 && cycles >= runRepeat)
            {
                StopRun("Fertig.");
                return;
            }

            phase = Phase.Waiting;
            notBeforeUtc = now.AddMilliseconds(Jitter(step.DelayAfterMs));
        }

        /// <summary>Zielfenster nicht im Vordergrund oder Pause-Taste: alles loslassen und warten.</summary>
        private void PauseRun()
        {
            paused = true;
            if (phase == Phase.Holding) SendUp(runSteps[stepIndex]);
            if (phase == Phase.Holding || phase == Phase.Typing)
            {
                phase = Phase.Gap;               // unterbrochenen Schritt nach dem Fortsetzen von vorn ausführen
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
            double ms = Jitter(runIntervalMs);
            ms = Math.Max(ms, MinIntervalMs);

            // Vom geplanten Zeitpunkt aus rechnen (kein Driften), aber Verpasstes nicht nachholen.
            DateTime next = previousDue.AddMilliseconds(ms);
            return next > now ? next : now.AddMilliseconds(ms);
        }

        /// <summary>Wert zufällig um ± „Zufällig variieren“-Prozent verändern.</summary>
        private double Jitter(double ms)
        {
            if (runJitter <= 0 || ms <= 0) return ms;
            return ms * (1.0 + (random.NextDouble() * 2.0 - 1.0) * runJitter / 100.0);
        }

        private int Jitter(int ms) => (int)Math.Round(Jitter((double)ms));

        private bool SendDown(Step step)
        {
            try
            {
                return KeySender.Down(step.Key, step.Ctrl, step.Alt, step.Shift, runScanCode);
            }
            catch (Exception ex)
            {
                Log.Error("Taste drücken", ex);
                return false;
            }
        }

        private void SendUp(Step step)
        {
            try
            {
                KeySender.Up(step.Key, step.Ctrl, step.Alt, step.Shift, runScanCode);
            }
            catch (Exception ex)
            {
                Log.Error("Taste loslassen", ex);
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
            SetTitle("AutoKey – läuft " + FormatDuration(now - runStartedUtc) + (userPaused ? " (Pause)" : string.Empty));

            if (sendWarning != null && !paused)
            {
                SetStatus(sendWarning);
                return;
            }

            string clickHint = runTarget.Length == 0 ? " Jetzt ins Zielfenster klicken!" : string.Empty;
            string text;

            if (userPaused)
            {
                text = "Pausiert – weiter mit " + (pauseHotkeyOk ? KeyNames.DescribeCombo(settings.PauseHotkey) : "„Weiter“ im Menü neben der Uhr") + ".";
            }
            else if (paused)
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
                    text = "Läuft – " + Count(presses) + limit + " Eingaben – nächste in "
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

        private void SetTitle(string text)
        {
            if (text == lastTitle) return;
            lastTitle = text;
            Text = text;
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

        /// <summary>Kurze Tonfolge (Frequenz, Dauer, …) im Hintergrund abspielen.</summary>
        private void Tone(int[] pattern)
        {
            if (!chkBeep.Checked) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    for (int i = 0; i + 1 < pattern.Length; i += 2) Console.Beep(pattern[i], pattern[i + 1]);
                }
                catch (Exception)
                {
                    // kein Lautsprecher – egal
                }
            });
        }

        private void Warn(string message) =>
            MessageBox.Show(this, message, "AutoKey", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        private void ShowAbout()
        {
            MessageBox.Show(
                "AutoKey " + AppVersion + (Settings.IsPortable ? " (portabel)" : string.Empty) + "\n\n"
                + "Drückt automatisch Tasten und Maustasten – aufgezeichnet oder als Ablauf, in festen Abständen, "
                + "zu einer bestimmten Uhrzeit oder dauerhaft gehalten.\n\n"
                + "Einstellungen, Profile und Fehlerprotokoll liegen in:\n" + Settings.BaseDirectory + "\n\n"
                + "Startoptionen:\n"
                + "  /start            – sofort mit den gespeicherten Einstellungen loslegen\n"
                + "  /tray             – unsichtbar im Infobereich starten\n"
                + "  /profile:Name     – dieses Profil laden",
                "Über AutoKey", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------ Fenster / Infobereich

        /// <summary>Wird aufgerufen, wenn AutoKey ein zweites Mal gestartet wurde.</summary>
        public void ShowFromOtherInstance()
        {
            RestoreFromTray();
            if (!running) SetStatus("AutoKey läuft bereits – hier ist das Fenster.");
        }

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
            if (recorder != null)
            {
                recorder.Dispose();
                recorder = null;
            }

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
                pickTimer.Dispose();
                tips.Dispose();
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
