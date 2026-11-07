using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AutoKey
{
    /// <summary>Eintrag für Auswahllisten: Taste plus deutscher Anzeigename.</summary>
    internal sealed class KeyItem
    {
        public KeyItem(Keys key)
        {
            Key = key;
            Name = KeyNames.GetName(key);
        }

        public Keys Key { get; }

        public string Name { get; }

        public override string ToString() => Name;
    }

    internal static class KeyNames
    {
        private static readonly Dictionary<Keys, string> Names = new Dictionary<Keys, string>
        {
            { Keys.Space, "Leertaste" },
            { Keys.LButton, "Maus links" },
            { Keys.RButton, "Maus rechts" },
            { Keys.MButton, "Maus Mitte" },
            { Keys.XButton1, "Maus Seitentaste 1" },
            { Keys.XButton2, "Maus Seitentaste 2" },
            { Keys.Enter, "Enter" },
            { Keys.Tab, "Tab" },
            { Keys.Escape, "Esc" },
            { Keys.Back, "Rücktaste" },
            { Keys.Up, "Pfeil hoch" },
            { Keys.Down, "Pfeil runter" },
            { Keys.Left, "Pfeil links" },
            { Keys.Right, "Pfeil rechts" },
            { Keys.PageUp, "Bild auf" },
            { Keys.PageDown, "Bild ab" },
            { Keys.Home, "Pos1" },
            { Keys.End, "Ende" },
            { Keys.Insert, "Einfg" },
            { Keys.Delete, "Entf" },
            { Keys.ShiftKey, "Shift" },
            { Keys.ControlKey, "Strg" },
            { Keys.Menu, "Alt" },
            { Keys.LWin, "Windows" },
            { Keys.Apps, "Kontextmenü" },
            { Keys.CapsLock, "Feststelltaste" },
            { Keys.NumLock, "Num-Lock" },
            { Keys.Scroll, "Rollen" },
            { Keys.Pause, "Pause" },
            { Keys.PrintScreen, "Druck" },
            { Keys.Multiply, "Num *" },
            { Keys.Add, "Num +" },
            { Keys.Subtract, "Num -" },
            { Keys.Divide, "Num /" },
            { Keys.Decimal, "Num ," },
            { Keys.VolumeUp, "Lauter" },
            { Keys.VolumeDown, "Leiser" },
            { Keys.VolumeMute, "Stumm" },
            { Keys.MediaPlayPause, "Wiedergabe/Pause" },
            { Keys.MediaNextTrack, "Nächster Titel" },
            { Keys.MediaPreviousTrack, "Vorheriger Titel" },
        };

        /// <summary>Tasten, die in der Auswahlliste angeboten werden.</summary>
        public static IEnumerable<Keys> CommonKeys
        {
            get
            {
                yield return Keys.Space;
                yield return Keys.LButton;
                yield return Keys.RButton;
                yield return Keys.MButton;
                yield return Keys.XButton1;
                yield return Keys.XButton2;
                yield return Keys.Enter;
                yield return Keys.Tab;
                yield return Keys.Escape;
                yield return Keys.Back;
                yield return Keys.Up;
                yield return Keys.Down;
                yield return Keys.Left;
                yield return Keys.Right;
                yield return Keys.PageUp;
                yield return Keys.PageDown;
                yield return Keys.Home;
                yield return Keys.End;
                yield return Keys.Insert;
                yield return Keys.Delete;
                for (Keys k = Keys.F1; k <= Keys.F12; k++) yield return k;
                for (Keys k = Keys.A; k <= Keys.Z; k++) yield return k;
                for (Keys k = Keys.D0; k <= Keys.D9; k++) yield return k;
                for (Keys k = Keys.NumPad0; k <= Keys.NumPad9; k++) yield return k;
                yield return Keys.Multiply;
                yield return Keys.Add;
                yield return Keys.Subtract;
                yield return Keys.Divide;
                yield return Keys.Decimal;
                yield return Keys.ShiftKey;
                yield return Keys.ControlKey;
                yield return Keys.Menu;
                yield return Keys.CapsLock;
                yield return Keys.VolumeUp;
                yield return Keys.VolumeDown;
                yield return Keys.VolumeMute;
                yield return Keys.MediaPlayPause;
                yield return Keys.MediaNextTrack;
                yield return Keys.MediaPreviousTrack;
            }
        }

        /// <summary>Tastenkombination inkl. Zusatztasten-Bits, z. B. Keys.Control | Keys.F6 → "Strg+F6".</summary>
        public static string DescribeCombo(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.None) return "(keine)";
            return Describe(code,
                (keyData & Keys.Control) != 0,
                (keyData & Keys.Alt) != 0,
                (keyData & Keys.Shift) != 0);
        }

        /// <summary>Taste mit Zusatztasten, z. B. "Strg+Shift+S".</summary>
        public static string Describe(Keys key, bool ctrl, bool alt, bool shift)
        {
            string text = GetName(key);
            if (shift) text = "Shift+" + text;
            if (alt) text = "Alt+" + text;
            if (ctrl) text = "Strg+" + text;
            return text;
        }

        public static string GetName(Keys key)
        {
            key &= Keys.KeyCode;

            if (key == Keys.None) return "(keine)";
            if (Names.TryGetValue(key, out string name)) return name;
            if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num " + (key - Keys.NumPad0);
            if (key >= Keys.A && key <= Keys.Z) return key.ToString();
            if (key >= Keys.F1 && key <= Keys.F24) return key.ToString();

            // Sonstige Tasten (z. B. Ü, Ö, ß, #) – Zeichen laut aktuellem Tastaturlayout ermitteln.
            try
            {
                uint ch = NativeMethods.MapVirtualKey((uint)key, NativeMethods.MAPVK_VK_TO_CHAR) & 0x7FFF;
                if (ch > 32) return ((char)ch).ToString();
            }
            catch (Exception)
            {
                // Fallback unten
            }

            return key.ToString();
        }
    }
}
