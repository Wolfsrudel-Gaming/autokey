using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AutoKey
{
    /// <summary>Kleines Fenster, das während der Aufzeichnung sichtbar bleibt.</summary>
    internal sealed class RecordForm : Form
    {
        private readonly MacroRecorder recorder;
        private readonly Label lblCount;
        private readonly CheckBox chkPositions;
        private readonly Timer refresh = new Timer { Interval = 250 };

        public RecordForm(MacroRecorder recorder, string hotkeyName, Icon icon)
        {
            this.recorder = recorder;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "AutoKey – Aufzeichnung";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;

            var lblInfo = new Label
            {
                Text = "● Aufzeichnung läuft.\n\n"
                       + "Jetzt im Spiel die Tasten, Mausklicks und das Mausrad genau so bedienen, wie es später "
                       + "wiederholt werden soll. Die Zeit läuft ab der ersten Eingabe.\n\n"
                       + "Beenden: " + hotkeyName + " drücken oder hier auf „Fertig“ klicken.",
                Location = new Point(12, 10),
                Size = new Size(336, 112),
            };
            lblCount = new Label { Text = "Aufgezeichnet: 0 Eingaben", Location = new Point(12, 126), AutoSize = true };
            chkPositions = new CheckBox
            {
                Text = "Mausklicks an der aufgezeichneten Bildschirmposition ausführen",
                Location = new Point(12, 150),
                AutoSize = true,
                Checked = true,
            };
            var btnDone = new Button { Text = "Fertig", DialogResult = DialogResult.OK, Bounds = new Rectangle(192, 184, 75, 27) };
            var btnCancel = new Button { Text = "Verwerfen", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(273, 184, 75, 27) };
            btnDone.Click += (s, e) => Close();
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { lblInfo, lblCount, chkPositions, btnDone, btnCancel });
            AcceptButton = btnDone;
            CancelButton = btnCancel;
            ClientSize = new Size(360, 223);

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
            ResumeLayout(false);

            refresh.Tick += (s, e) =>
                lblCount.Text = "Aufgezeichnet: " + recorder.Count.ToString(CultureInfo.CurrentCulture) + " Eingaben";
            refresh.Start();
        }

        /// <summary>Mausklicks an der aufgezeichneten Position ausführen?</summary>
        public bool UsePositions => chkPositions.Checked;

        /// <summary>Per Hotkey beendet: wie „Fertig“.</summary>
        public void Finish()
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) refresh.Dispose();
            base.Dispose(disposing);
        }
    }
}
