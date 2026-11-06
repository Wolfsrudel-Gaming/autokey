using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoKey
{
    /// <summary>Simuliert Tastendrücke über SendInput.</summary>
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
            inputs.Add(Make(key, false, scanCode));
            return Send(inputs);
        }

        /// <summary>Lässt Taste und Zusatztasten (in umgekehrter Reihenfolge) wieder los.</summary>
        public static bool Up(Keys key, bool ctrl, bool alt, bool shift, bool scanCode)
        {
            var inputs = new List<NativeMethods.INPUT>(4);
            inputs.Add(Make(key, true, scanCode));
            if (shift) inputs.Add(Make(Keys.ShiftKey, true, scanCode));
            if (alt) inputs.Add(Make(Keys.Menu, true, scanCode));
            if (ctrl) inputs.Add(Make(Keys.ControlKey, true, scanCode));
            return Send(inputs);
        }

        private static bool Send(List<NativeMethods.INPUT> inputs)
        {
            NativeMethods.INPUT[] array = inputs.ToArray();
            return NativeMethods.SendInput((uint)array.Length, array, InputSize) == array.Length;
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
                        time = 0,
                        dwExtraInfo = IntPtr.Zero,
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
