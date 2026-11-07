using System.Drawing;
using System.Windows.Forms;

namespace AutoKey
{
    /// <summary>Kleiner Dialog zur Eingabe eines Textes (z. B. Profilname).</summary>
    internal static class PromptDialog
    {
        /// <returns>Eingegebener Text oder null bei „Abbrechen“.</returns>
        public static string Show(IWin32Window owner, string title, string prompt, string defaultValue)
        {
            using (var form = new Form())
            {
                form.SuspendLayout();
                form.AutoScaleDimensions = new SizeF(96F, 96F);
                form.AutoScaleMode = AutoScaleMode.Dpi;
                form.Font = new Font("Segoe UI", 9F);
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.CenterParent;

                var label = new Label { Text = prompt, AutoSize = true, Location = new Point(12, 12) };
                var input = new TextBox { Text = defaultValue, Location = new Point(12, 36), Width = 336, MaxLength = 60 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(192, 72, 75, 27) };
                var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(273, 72, 75, 27) };

                form.Controls.AddRange(new Control[] { label, input, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.ClientSize = new Size(360, 111);
                form.ResumeLayout(false);
                form.PerformLayout();

                return form.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
            }
        }
    }
}
