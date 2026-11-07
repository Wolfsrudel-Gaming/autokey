using System;
using System.Globalization;
using System.Windows.Forms;

namespace AutoKey
{
    /// <summary>Ein Schritt eines Ablaufs: Taste bzw. Maustaste drücken, halten, danach warten.</summary>
    internal sealed class Step
    {
        public const int MaxHoldMs = 600_000;         // 10 Minuten
        public const int MaxDelayAfterMs = 3_600_000;  // 1 Stunde

        public Keys Key = Keys.Space;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public int HoldMs = 50;
        public int DelayAfterMs;

        public Step Clone() => (Step)MemberwiseClone();

        /// <summary>z. B. "Strg+S" oder "Maus links".</summary>
        public string KeyText => KeyNames.Describe(Key, Ctrl, Alt, Shift);

        /// <summary>Text für die Ablaufliste.</summary>
        public string Describe()
        {
            string text = KeyText + "   (" + HoldMs.ToString(CultureInfo.CurrentCulture) + " ms)";
            if (DelayAfterMs > 0) text += "   →  " + FormatMs(DelayAfterMs) + " warten";
            return text;
        }

        public string Serialize() =>
            string.Join(",",
                ((int)Key).ToString(CultureInfo.InvariantCulture),
                Ctrl ? "1" : "0",
                Alt ? "1" : "0",
                Shift ? "1" : "0",
                HoldMs.ToString(CultureInfo.InvariantCulture),
                DelayAfterMs.ToString(CultureInfo.InvariantCulture));

        public static Step Parse(string value)
        {
            string[] parts = value.Split(',');
            if (parts.Length < 6) return null;

            int[] numbers = new int[6];
            for (int i = 0; i < 6; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i])) return null;
            }

            if (numbers[0] <= 0 || numbers[0] > 255) return null;

            return new Step
            {
                Key = (Keys)numbers[0],
                Ctrl = numbers[1] == 1,
                Alt = numbers[2] == 1,
                Shift = numbers[3] == 1,
                HoldMs = Math.Max(1, Math.Min(MaxHoldMs, numbers[4])),
                DelayAfterMs = Math.Max(0, Math.Min(MaxDelayAfterMs, numbers[5])),
            };
        }

        private static string FormatMs(int ms) =>
            ms < 1000
                ? ms.ToString(CultureInfo.CurrentCulture) + " ms"
                : (ms / 1000.0).ToString("0.###", CultureInfo.CurrentCulture) + " s";
    }
}
