using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

    /// <summary>Einstellungen, gespeichert unter %APPDATA%\AutoKey\settings.ini.</summary>
    internal sealed class Settings
    {
        private const string TimeFormat = @"hh\:mm\:ss";

        public Keys Key = Keys.Space;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public int HoldMs = 50;
        public bool ScanCode;

        public int Interval = 5;
        public TimeUnit Unit = TimeUnit.Seconds;
        public int JitterPercent;
        public long Repeat;

        public bool StartAtTime;
        public int StartDelaySeconds = 5;
        public TimeSpan StartTime = new TimeSpan(12, 0, 0);
        public bool StopAtTime;
        public TimeSpan StopTime = new TimeSpan(18, 0, 0);

        public Keys Hotkey = Keys.F6;
        public bool TopMost;
        public bool MinimizeToTray = true;
        public bool PreventSleep = true;

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoKey", "settings.ini");

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

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return settings;

                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.TrimStart().StartsWith(";")) continue;
                    settings.Apply(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
                }
            }
            catch (Exception)
            {
                // Defekte oder nicht lesbare Datei: Standardwerte verwenden.
            }

            return settings;
        }

        public void Save()
        {
            var lines = new List<string>
            {
                "; AutoKey-Einstellungen",
                "Key=" + (int)Key,
                "Ctrl=" + Bool(Ctrl),
                "Alt=" + Bool(Alt),
                "Shift=" + Bool(Shift),
                "HoldMs=" + Int(HoldMs),
                "ScanCode=" + Bool(ScanCode),
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
                "PreventSleep=" + Bool(PreventSleep),
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, lines, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Speichern ist nicht kritisch.
            }
        }

        private void Apply(string name, string value)
        {
            switch (name)
            {
                case "Key": Key = ParseKey(value, Key); break;
                case "Ctrl": Ctrl = ParseBool(value, Ctrl); break;
                case "Alt": Alt = ParseBool(value, Alt); break;
                case "Shift": Shift = ParseBool(value, Shift); break;
                case "HoldMs": HoldMs = ParseInt(value, HoldMs); break;
                case "ScanCode": ScanCode = ParseBool(value, ScanCode); break;
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
                case "PreventSleep": PreventSleep = ParseBool(value, PreventSleep); break;
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
