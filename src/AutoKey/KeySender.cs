using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoKey
{
    /// <summary>Simuliert Tastendrücke, Mausklicks, Mausrad und Texteingabe über SendInput.</summary>
    internal static class KeySender
    {
        private static readonly int InputSize = Marshal.SizeOf(typeof(NativeMethods.INPUT));

        /// <summary>Drückt Zusatztasten und Taste herunter. Liefert false, wenn Windows die Eingabe blockiert hat.</summary>
        public static bool Down(Keys key, bool ctrl, bool alt, bool shift, bool scanCode)
        {
            var inputs = new List<NativeMethods.INPUT>(4);
            if (ctrl) inputs.Add(Make(Keys.ControlKey, false, scanCode));
            if (alt) inputs.Add(Make(Keys.Menu, false, scanCode));
            if (shift) inputs.Add(Make(Keys.ShiftKey, false, scanCode));
            inputs.Add(IsMouseButton(key) ? MakeMouse(key, false) : Make(key, false, scanCode));
            return Send(inputs);
        }

        /// <summary>Lässt Taste und Zusatztasten (in umgekehrter Reihenfolge) wieder los.</summary>
        public static bool Up(Keys key, bool ctrl, bool alt, bool shift, bool scanCode)
        {
            var inputs = new List<NativeMethods.INPUT>(4);
            inputs.Add(IsMouseButton(key) ? MakeMouse(key, true) : Make(key, true, scanCode));
            if (shift) inputs.Add(Make(Keys.ShiftKey, true, scanCode));
            if (alt) inputs.Add(Make(Keys.Menu, true, scanCode));
            if (ctrl) inputs.Add(Make(Keys.ControlKey, true, scanCode));
            return Send(inputs);
        }

        /// <summary>Mauszeiger auf eine Bildschirmposition setzen.</summary>
        public static bool MoveTo(int x, int y)
        {
            try
            {
                return NativeMethods.SetCursorPos(x, y);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Mausrad drehen: positive Werte = nach oben, negative = nach unten (in Rasten).</summary>
        public static bool Wheel(int notches)
        {
            if (notches == 0) return true;
            var input = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        mouseData = unchecked((uint)(notches * NativeMethods.WHEEL_DELTA)),
                        dwFlags = NativeMethods.MOUSEEVENTF_WHEEL,
                    },
                },
            };
            return Send(new List<NativeMethods.INPUT> { input });
        }

        /// <summary>
        /// Tippt das Zeichen an Position <paramref name="index"/> (bei Emojis u. ä. auch zwei Zeichen auf einmal).
        /// Liefert die Zahl der verbrauchten Zeichen; 0 bei Fehler.
        /// </summary>
        public static int TypeCharacter(string text, int index)
        {
            if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length) return 0;

            char c = text[index];
            var inputs = new List<NativeMethods.INPUT>(4);
            int consumed = 1;

            if (c == '\r' || c == '\n')
            {
                // Zeilenumbruch als Enter senden; "\r\n" zählt als ein Zeichen.
                if (c == '\r' && index + 1 < text.Length && text[index + 1] == '\n') consumed = 2;
                inputs.Add(Make(Keys.Enter, false, false));
                inputs.Add(Make(Keys.Enter, true, false));
            }
            else if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                consumed = 2;
                inputs.Add(MakeUnicode(c, false));
                inputs.Add(MakeUnicode(text[index + 1], false));
                inputs.Add(MakeUnicode(c, true));
                inputs.Add(MakeUnicode(text[index + 1], true));
            }
            else
            {
                inputs.Add(MakeUnicode(c, false));
                inputs.Add(MakeUnicode(c, true));
            }

            return Send(inputs) ? consumed : 0;
        }

        public static bool IsMouseButton(Keys key)
        {
            switch (key & Keys.KeyCode)
            {
                case Keys.LButton:
                case Keys.RButton:
                case Keys.MButton:
                case Keys.XButton1:
                case Keys.XButton2:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Mausklick an der aktuellen Mausposition.</summary>
        private static NativeMethods.INPUT MakeMouse(Keys button, bool up)
        {
            uint flags;
            uint data = 0;
            switch (button & Keys.KeyCode)
            {
                case Keys.RButton:
                    flags = up ? NativeMethods.MOUSEEVENTF_RIGHTUP : NativeMethods.MOUSEEVENTF_RIGHTDOWN;
                    break;
                case Keys.MButton:
                    flags = up ? NativeMethods.MOUSEEVENTF_MIDDLEUP : NativeMethods.MOUSEEVENTF_MIDDLEDOWN;
                    break;
                case Keys.XButton1:
                    flags = up ? NativeMethods.MOUSEEVENTF_XUP : NativeMethods.MOUSEEVENTF_XDOWN;
                    data = NativeMethods.XBUTTON1;
                    break;
                case Keys.XButton2:
                    flags = up ? NativeMethods.MOUSEEVENTF_XUP : NativeMethods.MOUSEEVENTF_XDOWN;
                    data = NativeMethods.XBUTTON2;
                    break;
                default:
                    flags = up ? NativeMethods.MOUSEEVENTF_LEFTUP : NativeMethods.MOUSEEVENTF_LEFTDOWN;
                    break;
            }

            return new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        mouseData = data,
                        dwFlags = flags,
                    },
                },
            };
        }

        private static NativeMethods.INPUT MakeUnicode(char c, bool up)
        {
            return new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0),
                    },
                },
            };
        }

        private static bool Send(List<NativeMethods.INPUT> inputs)
        {
            try
            {
                NativeMethods.INPUT[] array = inputs.ToArray();
                return NativeMethods.SendInput((uint)array.Length, array, InputSize) == array.Length;
            }
            catch (Exception ex)
            {
                Log.Error("Eingabe senden", ex);
                return false;
            }
        }

        private static NativeMethods.INPUT Make(Keys key, bool up, bool useScanCode)
        {
            ushort vk = (ushort)(key & Keys.KeyCode);
            uint scanEx = NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC_EX);
            ushort scan = (ushort)(scanEx & 0xFF);
            uint prefix = (scanEx >> 8) & 0xFF;

            uint flags = up ? NativeMethods.KEYEVENTF_KEYUP : 0;
            if (prefix == 0xE0 || prefix == 0xE1 || IsExtended(key))
                flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            if (useScanCode && scan != 0)
                flags |= NativeMethods.KEYEVENTF_SCANCODE;

            return new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = scan,
                        dwFlags = flags,
                    },
                },
            };
        }

        private static bool IsExtended(Keys key)
        {
            switch (key & Keys.KeyCode)
            {
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.End:
                case Keys.Home:
                case Keys.Left:
                case Keys.Up:
                case Keys.Right:
                case Keys.Down:
                case Keys.Insert:
                case Keys.Delete:
                case Keys.Divide:
                case Keys.NumLock:
                case Keys.RControlKey:
                case Keys.RMenu:
                case Keys.LWin:
                case Keys.RWin:
                case Keys.Apps:
                case Keys.VolumeMute:
                case Keys.VolumeDown:
                case Keys.VolumeUp:
                case Keys.MediaNextTrack:
                case Keys.MediaPreviousTrack:
                case Keys.MediaStop:
                case Keys.MediaPlayPause:
                    return true;
                default:
                    return false;
            }
        }
    }
}
