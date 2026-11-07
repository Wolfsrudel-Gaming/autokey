using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AutoKey
{
    internal static class Program
    {
        private const string MutexName = @"Local\AutoKey-SingleInstance-5d0c7f2e";
        private const string ShowEventName = @"Local\AutoKey-Show-5d0c7f2e";

        /// <summary>
        /// Einstiegspunkt.
        /// Optionen:  /start          – sofort mit den gespeicherten Einstellungen loslegen
        ///            /tray           – unsichtbar im Infobereich starten
        ///            /profile:Name   – dieses Profil laden
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, MutexName, out bool isFirstInstance))
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            {
                if (!isFirstInstance)
                {
                    // Läuft schon: das vorhandene Fenster nach vorn holen und beenden.
                    showEvent.Set();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) =>
                {
                    Log.Error("Unerwarteter Fehler", e.Exception);
                    MessageBox.Show("Unerwarteter Fehler:\n\n" + e.Exception.Message + "\n\nDetails stehen in:\n" + Log.FilePath,
                        "AutoKey", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    Log.Error("Schwerer Fehler", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));

                var form = new MainForm(HasArg(args, "start"), HasArg(args, "tray"), GetArgValue(args, "profile"));

                // Zweite Instanz gestartet? Dann Fenster zeigen.
                var watcher = new Thread(() =>
                {
                    while (true)
                    {
                        showEvent.WaitOne();
                        try
                        {
                            form.BeginInvoke(new Action(form.ShowFromOtherInstance));
                        }
                        catch (Exception)
                        {
                            // Fenster wird gerade geschlossen
                        }
                    }
                })
                { IsBackground = true, Name = "AutoKey-ShowWatcher" };
                watcher.Start();

                Application.Run(form);
                GC.KeepAlive(mutex);
            }
        }

        private static bool HasArg(string[] args, string name) =>
            args.Any(a => a.TrimStart('/', '-').Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Wert von "/name:Wert" oder "/name=Wert".</summary>
        private static string GetArgValue(string[] args, string name)
        {
            foreach (string arg in args)
            {
                string a = arg.TrimStart('/', '-');
                int sep = a.IndexOfAny(new[] { ':', '=' });
                if (sep > 0 && a.Substring(0, sep).Equals(name, StringComparison.OrdinalIgnoreCase))
                    return a.Substring(sep + 1).Trim().Trim('"');
            }

            return null;
        }
    }
}
