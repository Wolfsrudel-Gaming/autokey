using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AutoKey
{
    internal enum TimeUnit
    {
        Milliseconds = 0,
        Seconds = 1,
        Minutes = 2,
        Hours = 3,
    }

    /// <summary>
    /// Einstellungen, gespeichert unter %APPDATA%\AutoKey\settings.ini.
    /// Profile (%APPDATA%\AutoKey\Profile\*.ini) enthalten nur den „Aufgaben“-Teil:
    /// Ablauf, Zeitplan, Zielfenster.
    /// </summary>
    internal sealed class Settings
    {
        private const string TimeFormat = @"hh\:mm\:ss";

        // ---- Aufgabe (auch in Profilen)
        public List<Step> Steps = new List<Step> { new Step() };
        public bool ScanCode;
        public string TargetProcess = string.Empty;
        public bool HoldMode;

        public int Interval = 5;
        public TimeUnit Unit = TimeUnit.Seconds;
        public int JitterPercent;
        public long Repeat;

        public bool StartAtTime;
        public int StartDelaySeconds = 5;
        public TimeSpan StartTime = new TimeSpan(12, 0, 0);
        public bool StopAtTime;
        public TimeSpan StopTime = new TimeSpan(18, 0, 0);

        // ---- Allgemein
        public Keys Hotkey = Keys.F6;
        public bool TopMost;
        public bool MinimizeToTray = true;
        public bool HideOnStart;
        public bool ShowNotifications = true;
        public bool PreventSleep = true;
        public string Profile = string.Empty;

        public static string BaseDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoKey");

        public static string FilePath => Path.Combine(BaseDirectory, "settings.ini");

        public static string ProfileDirectory => Path.Combine(BaseDirectory, "Profile");

        public double IntervalMilliseconds
        {
            get
            {
                switch (Unit)
                {
                    case TimeUnit.Milliseconds: return Interval;
                    case TimeUnit.Minutes: return Interval * 60_000.0;
                    case TimeUnit.Hours: return Interval * 3_600_000.0;
                    default: return Interval * 1_000.0;
                }
            }
        }

        public static Settings Load() => LoadFrom(FilePath);

        public void Save() => SaveTo(FilePath);

        // ------------------------------------------------------------------ Profile

        public static List<string> ListProfiles()
        {
            try
            {
                if (!Directory.Exists(ProfileDirectory)) return new List<string>();
                return Directory.GetFiles(ProfileDirectory, "*.ini")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        /// <summary>Entfernt Zeichen, die in Dateinamen nicht erlaubt sind.</summary>
        public static string CleanProfileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), string.Empty);
            return name.Trim().TrimEnd('.');
        }

        public static bool ProfileExists(string name) => File.Exists(ProfilePath(name));

        public static Settings LoadProfile(string name) => LoadFrom(ProfilePath(name));

        public bool SaveProfile(string name) => SaveTo(ProfilePath(name));

        public static void DeleteProfile(string name)
        {
            try
            {
                File.Delete(ProfilePath(name));
            }
            catch (Exception)
            {
                // ignorieren
            }
        }

        private static string ProfilePath(string name) => Path.Combine(ProfileDirectory, name + ".ini");

        /// <summary>Übernimmt Ablauf, Zeitplan und Zielfenster aus einem Profil.</summary>
        public void CopyTaskFrom(Settings other)
        {
            Steps = other.Steps.Select(s => s.Clone()).ToList();
            ScanCode = other.ScanCode;
            TargetProcess = other.TargetProcess;
            HoldMode = other.HoldMode;
            Interval = other.Interval;
            Unit = other.Unit;
            JitterPercent = other.JitterPercent;
            Repeat = other.Repeat;
            StartAtTime = other.StartAtTime;
            StartDelaySeconds = other.StartDelaySeconds;
            StartTime = other.StartTime;
            StopAtTime = other.StopAtTime;
            StopTime = other.StopTime;
        }

        // ------------------------------------------------------------------ Lesen / Schreiben

        private static Settings LoadFrom(string path)
        {
            var settings = new Settings();
            var legacyStep = new Step();   // Format von Version 1.0 (nur eine Taste)
            var stepLines = new SortedDictionary<int, string>();

            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0 || line.TrimStart().StartsWith(";")) continue;

                        string name = line.Substring(0, eq).Trim();
                        string value = line.Substring(eq + 1).Trim();

                        if (name.StartsWith("Step", StringComparison.Ordinal)
                            && int.TryParse(name.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                        {
                            stepLines[index] = value;
                        }
                        else
                        {
                            settings.Apply(name, value, legacyStep);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Defekte oder nicht lesbare Datei: Standardwerte verwenden.
            }

            settings.Steps = stepLines.Count > 0
                ? stepLines.Values.Select(Step.Parse).Where(s => s != null).ToList()
                : new List<Step> { legacyStep };
            if (settings.Steps.Count == 0) settings.Steps.Add(new Step());

            return settings;
        }

        private bool SaveTo(string path)
        {
            var lines = new List<string>
            {
                "; AutoKey-Einstellungen",
                "ScanCode=" + Bool(ScanCode),
                "TargetProcess=" + TargetProcess,
                "HoldMode=" + Bool(HoldMode),
                "Interval=" + Int(Interval),
                "Unit=" + (int)Unit,
                "JitterPercent=" + Int(JitterPercent),
                "Repeat=" + Repeat.ToString(CultureInfo.InvariantCulture),
                "StartAtTime=" + Bool(StartAtTime),
                "StartDelaySeconds=" + Int(StartDelaySeconds),
                "StartTime=" + StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
                "StopAtTime=" + Bool(StopAtTime),
                "StopTime=" + StopTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
                "Hotkey=" + (int)Hotkey,
                "TopMost=" + Bool(TopMost),
                "MinimizeToTray=" + Bool(MinimizeToTray),
                "HideOnStart=" + Bool(HideOnStart),
                "ShowNotifications=" + Bool(ShowNotifications),
                "PreventSleep=" + Bool(PreventSleep),
                "Profile=" + Profile,
                "; Schritte: Taste,Strg,Alt,Shift,Haltedauer ms,danach warten ms",
            };

            for (int i = 0; i < Steps.Count; i++)
            {
                lines.Add("Step" + (i + 1).ToString(CultureInfo.InvariantCulture) + "=" + Steps[i].Serialize());
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, lines, Encoding.UTF8);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void Apply(string name, string value, Step legacyStep)
        {
            switch (name)
            {
                // Version 1.0: eine einzelne Taste
                case "Key": legacyStep.Key = ParseKey(value, legacyStep.Key); break;
                case "Ctrl": legacyStep.Ctrl = ParseBool(value, legacyStep.Ctrl); break;
                case "Alt": legacyStep.Alt = ParseBool(value, legacyStep.Alt); break;
                case "Shift": legacyStep.Shift = ParseBool(value, legacyStep.Shift); break;
                case "HoldMs": legacyStep.HoldMs = Math.Max(1, Math.Min(Step.MaxHoldMs, ParseInt(value, legacyStep.HoldMs))); break;

                case "ScanCode": ScanCode = ParseBool(value, ScanCode); break;
                case "TargetProcess": TargetProcess = value; break;
                case "HoldMode": HoldMode = ParseBool(value, HoldMode); break;
                case "Interval": Interval = ParseInt(value, Interval); break;
                case "Unit":
                    int unit = ParseInt(value, (int)Unit);
                    if (unit >= 0 && unit <= 3) Unit = (TimeUnit)unit;
                    break;
                case "JitterPercent": JitterPercent = ParseInt(value, JitterPercent); break;
                case "Repeat":
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long repeat)) Repeat = repeat;
                    break;
                case "StartAtTime": StartAtTime = ParseBool(value, StartAtTime); break;
                case "StartDelaySeconds": StartDelaySeconds = ParseInt(value, StartDelaySeconds); break;
                case "StartTime": StartTime = ParseTime(value, StartTime); break;
                case "StopAtTime": StopAtTime = ParseBool(value, StopAtTime); break;
                case "StopTime": StopTime = ParseTime(value, StopTime); break;
                case "Hotkey": Hotkey = ParseKey(value, Hotkey); break;
                case "TopMost": TopMost = ParseBool(value, TopMost); break;
                case "MinimizeToTray": MinimizeToTray = ParseBool(value, MinimizeToTray); break;
                case "HideOnStart": HideOnStart = ParseBool(value, HideOnStart); break;
                case "ShowNotifications": ShowNotifications = ParseBool(value, ShowNotifications); break;
                case "PreventSleep": PreventSleep = ParseBool(value, PreventSleep); break;
                case "Profile": Profile = value; break;
            }
        }

        private static string Bool(bool value) => value ? "1" : "0";

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static bool ParseBool(string value, bool fallback) =>
            value == "1" ? true : value == "0" ? false : fallback;

        private static int ParseInt(string value, int fallback) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;

        private static Keys ParseKey(string value, Keys fallback)
        {
            int code = ParseInt(value, -1);
            return code > 0 && code < 256 ? (Keys)code : fallback;
        }

        private static TimeSpan ParseTime(string value, TimeSpan fallback) =>
            TimeSpan.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture, out TimeSpan result)
                && result >= TimeSpan.Zero && result < TimeSpan.FromDays(1)
                ? result
                : fallback;
    }
}
