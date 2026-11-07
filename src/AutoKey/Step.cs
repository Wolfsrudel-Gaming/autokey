using System;
using System.Globalization;
using System.Windows.Forms;

namespace AutoKey
{
    internal enum StepType
    {
        Key = 0,    // Taste oder Maustaste drücken und halten
        Text = 1,   // Text eintippen
        Wheel = 2,  // Mausrad drehen
    }

    /// <summary>Ein Schritt eines Ablaufs.</summary>
    internal sealed class Step
    {
        public const int MaxHoldMs = 600_000;          // 10 Minuten
        public const int MaxDelayAfterMs = 3_600_000;   // 1 Stunde
        public const int MaxWheel = 100;
        public const int MaxTextLength = 500;

        public StepType Type = StepType.Key;
        public Keys Key = Keys.Space;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;

        /// <summary>Haltedauer der Taste – bei Text: Millisekunden pro Zeichen.</summary>
        public int HoldMs = 50;

        public int DelayAfterMs;

        /// <summary>Mausklick bzw. Mausrad an einer festen Bildschirmposition ausführen.</summary>
        public bool MouseFixed;

        public int MouseX;
        public int MouseY;

        /// <summary>Mausrad-Rasten: positiv = hoch, negativ = runter.</summary>
        public int Wheel = 3;

        public string Text = string.Empty;

        public Step Clone() => (Step)MemberwiseClone();

        public bool IsMouseButton => Type == StepType.Key && KeySender.IsMouseButton(Key);

        /// <summary>Ob der Schritt die Maus vorher an eine feste Position setzt.</summary>
        public bool UsesMousePosition => MouseFixed && (IsMouseButton || Type == StepType.Wheel);

        /// <summary>z. B. "Strg+S" oder "Maus links".</summary>
        public string KeyText => KeyNames.Describe(Key, Ctrl, Alt, Shift);

        /// <summary>Kurzbeschreibung für die Ablaufliste.</summary>
        public string Describe()
        {
            string text;
            switch (Type)
            {
                case StepType.Text:
                    text = "Text „" + Shorten(Text, 24) + "“   (" + Ms(HoldMs) + "/Zeichen)";
                    break;
                case StepType.Wheel:
                    text = "Mausrad " + Math.Abs(Wheel).ToString(CultureInfo.CurrentCulture) + (Wheel >= 0 ? " hoch" : " runter");
                    break;
                default:
                    text = KeyText + "   (" + Ms(HoldMs) + ")";
                    break;
            }

            if (UsesMousePosition) text += "   bei " + MouseX + "," + MouseY;
            if (DelayAfterMs > 0) text += "   →  " + FormatMs(DelayAfterMs) + " warten";
            return text;
        }

        /// <summary>Format: Taste,Strg,Alt,Shift,Haltedauer,Warten,Art,MausFest,X,Y,Rad,Text (Text zuletzt, darf Kommas enthalten).</summary>
        public string Serialize() =>
            string.Join(",",
                Int((int)Key),
                Ctrl ? "1" : "0",
                Alt ? "1" : "0",
                Shift ? "1" : "0",
                Int(HoldMs),
                Int(DelayAfterMs),
                Int((int)Type),
                MouseFixed ? "1" : "0",
                Int(MouseX),
                Int(MouseY),
                Int(Wheel),
                Text.Replace("\r", " ").Replace("\n", " "));

        public static Step Parse(string value)
        {
            string[] parts = value.Split(',');
            if (parts.Length < 6) return null;

            int[] n = new int[11];
            for (int i = 0; i < Math.Min(parts.Length, 11); i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i]))
                {
                    if (i < 6) return null;
                    n[i] = i == 10 ? 3 : 0;
                }
            }

            if (n[0] <= 0 || n[0] > 255) return null;
            if (parts.Length < 11) n[10] = 3;

            var step = new Step
            {
                Key = (Keys)n[0],
                Ctrl = n[1] == 1,
                Alt = n[2] == 1,
                Shift = n[3] == 1,
                HoldMs = Math.Max(1, Math.Min(MaxHoldMs, n[4])),
                DelayAfterMs = Math.Max(0, Math.Min(MaxDelayAfterMs, n[5])),
                Type = n[6] >= 0 && n[6] <= 2 ? (StepType)n[6] : StepType.Key,
                MouseFixed = n[7] == 1,
                MouseX = n[8],
                MouseY = n[9],
                Wheel = Math.Max(-MaxWheel, Math.Min(MaxWheel, n[10])),
            };

            if (parts.Length > 11)
            {
                string text = string.Join(",", parts, 11, parts.Length - 11);
                step.Text = text.Length > MaxTextLength ? text.Substring(0, MaxTextLength) : text;
            }

            return step;
        }

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Ms(int ms) => ms.ToString(CultureInfo.CurrentCulture) + " ms";

        private static string Shorten(string text, int max) =>
            text.Length <= max ? text : text.Substring(0, max - 1) + "…";

        private static string FormatMs(int ms) =>
            ms < 1000
                ? ms.ToString(CultureInfo.CurrentCulture) + " ms"
                : (ms / 1000.0).ToString("0.###", CultureInfo.CurrentCulture) + " s";
    }
}
