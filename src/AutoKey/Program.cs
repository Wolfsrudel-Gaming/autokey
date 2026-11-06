using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AutoKey
{
    internal static class Program
    {
        /// <summary>
        /// Einstiegspunkt.
        /// Optionen:  /start  – sofort mit den gespeicherten Einstellungen loslegen
        ///            /tray   – minimiert im Infobereich starten
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, @"Local\AutoKey-SingleInstance-5d0c7f2e", out bool isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    MessageBox.Show("AutoKey läuft bereits (siehe Taskleiste bzw. Infobereich neben der Uhr).",
                        "AutoKey", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) =>
                    MessageBox.Show("Unerwarteter Fehler:\n\n" + e.Exception.Message,
                        "AutoKey", MessageBoxButtons.OK, MessageBoxIcon.Error);

                bool start = HasArg(args, "start");
                bool tray = HasArg(args, "tray");
                Application.Run(new MainForm(start, tray));

                GC.KeepAlive(mutex);
            }
        }

        private static bool HasArg(string[] args, string name) =>
            args.Any(a => a.TrimStart('/', '-').Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
