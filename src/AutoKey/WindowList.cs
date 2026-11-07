using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AutoKey
{
    /// <summary>Eintrag für die Auswahl des Zielfensters.</summary>
    internal sealed class WindowItem
    {
        public static readonly WindowItem Any = new WindowItem(string.Empty, "(jedes aktive Fenster)", true);

        public WindowItem(string processName, string title, bool running)
        {
            ProcessName = processName;
            Title = title;
            Running = running;
        }

        /// <summary>Prozessname ohne ".exe"; leer = jedes Fenster.</summary>
        public string ProcessName { get; }

        public string Title { get; }

        public bool Running { get; }

        public override string ToString()
        {
            if (ProcessName.Length == 0) return Title;
            if (!Running) return ProcessName + ".exe   (läuft gerade nicht)";
            return Title + "   (" + ProcessName + ".exe)";
        }
    }

    /// <summary>Hilfsfunktionen rund um Programmfenster.</summary>
    internal static class WindowList
    {
        private static uint cachedPid;
        private static string cachedName = string.Empty;

        /// <summary>Alle Programme mit sichtbarem Hauptfenster (ein Eintrag pro Programm).</summary>
        public static List<WindowItem> GetWindows()
        {
            var result = new List<WindowItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int self = Process.GetCurrentProcess().Id;

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == IntPtr.Zero) continue;
                    string title = process.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title) || !seen.Add(process.ProcessName)) continue;
                    result.Add(new WindowItem(process.ProcessName, title, true));
                }
                catch (Exception)
                {
                    // Prozess beendet sich gerade oder ist nicht zugänglich – überspringen.
                }
                finally
                {
                    process.Dispose();
                }
            }

            result.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
            return result;
        }

        /// <summary>Prozessname (ohne ".exe") des Fensters, das gerade im Vordergrund ist.</summary>
        public static string ForegroundProcessName()
        {
            try
            {
                IntPtr window = NativeMethods.GetForegroundWindow();
                if (window == IntPtr.Zero) return string.Empty;

                NativeMethods.GetWindowThreadProcessId(window, out uint pid);
                if (pid == 0) return string.Empty;

                if (pid != cachedPid)
                {
                    using (Process process = Process.GetProcessById((int)pid))
                    {
                        cachedName = process.ProcessName;
                    }

                    cachedPid = pid;
                }

                return cachedName;
            }
            catch (Exception)
            {
                cachedPid = 0;
                return string.Empty;
            }
        }

        public static bool IsRunning(string processName)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                foreach (Process p in processes) p.Dispose();
                return processes.Length > 0;
            }
            catch (Exception)
            {
                return true; // im Zweifel nicht abbrechen
            }
        }
    }
}
