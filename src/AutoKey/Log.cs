using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AutoKey
{
    /// <summary>Fehlerprotokoll (autokey.log im Einstellungsordner) – hilft bei der Fehlersuche.</summary>
    internal static class Log
    {
        private const long MaxBytes = 256 * 1024;
        private static readonly object Gate = new object();

        public static string FilePath => Path.Combine(Settings.BaseDirectory, "autokey.log");

        public static void Error(string context, Exception ex) => Write("FEHLER " + context + ": " + ex);

        public static void Info(string message) => Write(message);

        private static void Write(string text)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Settings.BaseDirectory);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes) info.Delete();
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + text + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // Protokollieren darf nie selbst zum Problem werden.
            }
        }
    }
}
