using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoKey
{
    /// <summary>
    /// Zeichnet Tastendrücke, Mausklicks und Mausrad systemweit auf (Low-Level-Hooks)
    /// und macht daraus Schritte für den Ablauf.
    /// </summary>
    internal sealed class MacroRecorder : IDisposable
    {
        private enum Kind
        {
            Down,
            Up,
            Wheel,
        }

        private struct Event
        {
            public Kind Kind;
            public Keys Key;
            public uint Time;
            public Point Point;
            public int Delta;
        }

        private readonly List<Event> events = new List<Event>();
        private readonly HashSet<Keys> ignoredKeys;
        private readonly Func<Point, bool> ignorePoint;
        private readonly NativeMethods.HookProc keyboardProc;
        private readonly NativeMethods.HookProc mouseProc;
        private IntPtr keyboardHook;
        private IntPtr mouseHook;
        private uint stopTime;

        /// <param name="ignoredKeys">Tasten, die nicht aufgezeichnet werden (z. B. die Start/Stopp-Taste).</param>
        /// <param name="ignorePoint">Liefert true für Mauspositionen, die ignoriert werden (z. B. über dem Aufnahmefenster).</param>
        public MacroRecorder(IEnumerable<Keys> ignoredKeys, Func<Point, bool> ignorePoint)
        {
            this.ignoredKeys = new HashSet<Keys>(ignoredKeys);
            this.ignorePoint = ignorePoint;
            keyboardProc = KeyboardCallback; // Delegates festhalten, sonst räumt sie der GC weg
            mouseProc = MouseCallback;
        }

        /// <summary>Zahl der aufgezeichneten Eingaben (Tastendrücke, Klicks, Rad-Rasten).</summary>
        public int Count { get; private set; }

        public bool IsRecording => keyboardHook != IntPtr.Zero;

        /// <summary>Hooks einhängen. Liefert false, wenn Windows das verweigert.</summary>
        public bool Start()
        {
            if (IsRecording) return true;
            events.Clear();
            Count = 0;

            try
            {
                IntPtr module = NativeMethods.GetModuleHandle(null);
                keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, keyboardProc, module, 0);
                mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, mouseProc, module, 0);
            }
            catch (Exception ex)
            {
                Log.Error("Aufzeichnung starten", ex);
                Stop();
                return false;
            }

            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
            {
                Stop();
                return false;
            }

            return true;
        }

        public void Stop()
        {
            stopTime = unchecked((uint)Environment.TickCount);
            try
            {
                if (keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(keyboardHook);
                if (mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(mouseHook);
            }
            catch (Exception)
            {
                // ignorieren
            }

            keyboardHook = IntPtr.Zero;
            mouseHook = IntPtr.Zero;
        }

        public void Dispose() => Stop();

        private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var data = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    int msg = wParam.ToInt32();
                    var key = (Keys)data.vkCode;
                    if ((data.flags & NativeMethods.LLKHF_INJECTED) == 0 && !ignoredKeys.Contains(key))
                    {
                        if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                            Add(new Event { Kind = Kind.Down, Key = key, Time = data.time });
                        else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                            Add(new Event { Kind = Kind.Up, Key = key, Time = data.time });
                    }
                }
                catch (Exception)
                {
                    // Aufzeichnung darf die Eingabe nie blockieren
                }
            }

            return NativeMethods.CallNextHookEx(keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var data = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                    var point = new Point(data.pt.x, data.pt.y);
                    if ((data.flags & NativeMethods.LLMHF_INJECTED) == 0 && !ignorePoint(point))
                    {
                        int msg = wParam.ToInt32();
                        int high = unchecked((short)(data.mouseData >> 16));
                        switch (msg)
                        {
                            case NativeMethods.WM_LBUTTONDOWN: AddMouse(Kind.Down, Keys.LButton, data.time, point); break;
                            case NativeMethods.WM_LBUTTONUP: AddMouse(Kind.Up, Keys.LButton, data.time, point); break;
                            case NativeMethods.WM_RBUTTONDOWN: AddMouse(Kind.Down, Keys.RButton, data.time, point); break;
                            case NativeMethods.WM_RBUTTONUP: AddMouse(Kind.Up, Keys.RButton, data.time, point); break;
                            case NativeMethods.WM_MBUTTONDOWN: AddMouse(Kind.Down, Keys.MButton, data.time, point); break;
                            case NativeMethods.WM_MBUTTONUP: AddMouse(Kind.Up, Keys.MButton, data.time, point); break;
                            case NativeMethods.WM_XBUTTONDOWN: AddMouse(Kind.Down, high == 2 ? Keys.XButton2 : Keys.XButton1, data.time, point); break;
                            case NativeMethods.WM_XBUTTONUP: AddMouse(Kind.Up, high == 2 ? Keys.XButton2 : Keys.XButton1, data.time, point); break;
                            case NativeMethods.WM_MOUSEWHEEL:
                                Add(new Event { Kind = Kind.Wheel, Time = data.time, Point = point, Delta = high });
                                break;
                        }
                    }
                }
                catch (Exception)
                {
                    // ignorieren
                }
            }

            return NativeMethods.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        private void AddMouse(Kind kind, Keys button, uint time, Point point) =>
            Add(new Event { Kind = kind, Key = button, Time = time, Point = point });

        private void Add(Event e)
        {
            events.Add(e);
            if (e.Kind != Kind.Up) Count++;
        }

        private static bool IsModifier(Keys key)
        {
            switch (key)
            {
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey:
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey:
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu:
                    return true;
                default:
                    return false;
            }
        }

        private static int Elapsed(uint from, uint to) => unchecked((int)(to - from));

        /// <summary>Aufzeichnung in Schritte umwandeln.</summary>
        /// <param name="usePositions">Mausklicks an der aufgezeichneten Bildschirmposition ausführen.</param>
        public List<Step> ToSteps(bool usePositions)
        {
            var result = new List<Timed>();
            var pending = new Dictionary<Keys, Timed>();
            bool ctrl = false, alt = false, shift = false;

            foreach (Event e in events)
            {
                if (IsModifier(e.Key) && e.Kind != Kind.Wheel)
                {
                    bool down = e.Kind == Kind.Down;
                    switch (e.Key)
                    {
                        case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey: shift = down; break;
                        case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey: ctrl = down; break;
                        default: alt = down; break;
                    }

                    continue;
                }

                switch (e.Kind)
                {
                    case Kind.Down:
                        if (pending.ContainsKey(e.Key)) break; // Tastenwiederholung beim Gedrückthalten
                        pending[e.Key] = new Timed
                        {
                            Step = new Step
                            {
                                Type = StepType.Key,
                                Key = e.Key,
                                Ctrl = ctrl,
                                Alt = alt,
                                Shift = shift,
                                MouseFixed = usePositions && KeySender.IsMouseButton(e.Key),
                                MouseX = e.Point.X,
                                MouseY = e.Point.Y,
                            },
                            Down = e.Time,
                        };
                        break;

                    case Kind.Up:
                        if (pending.TryGetValue(e.Key, out Timed timed))
                        {
                            pending.Remove(e.Key);
                            timed.Up = e.Time;
                            timed.Step.HoldMs = Clamp(Elapsed(timed.Down, timed.Up), 1, Step.MaxHoldMs);
                            result.Add(timed);
                        }

                        break;

                    case Kind.Wheel:
                        int notches = e.Delta / NativeMethods.WHEEL_DELTA;
                        if (notches == 0) break;

                        // Mehrere Rad-Rasten kurz hintereinander in dieselbe Richtung zusammenfassen
                        Timed last = result.Count > 0 ? result[result.Count - 1] : null;
                        if (last != null && last.Step.Type == StepType.Wheel
                            && Math.Sign(last.Step.Wheel) == Math.Sign(notches)
                            && Elapsed(last.Up, e.Time) < 400)
                        {
                            last.Step.Wheel = Clamp(last.Step.Wheel + notches, -Step.MaxWheel, Step.MaxWheel);
                            last.Up = e.Time;
                            break;
                        }

                        result.Add(new Timed
                        {
                            Step = new Step
                            {
                                Type = StepType.Wheel,
                                Wheel = Clamp(notches, -Step.MaxWheel, Step.MaxWheel),
                                MouseFixed = usePositions,
                                MouseX = e.Point.X,
                                MouseY = e.Point.Y,
                            },
                            Down = e.Time,
                            Up = e.Time,
                        });
                        break;
                }
            }

            // Beim Stoppen noch gehaltene Tasten
            foreach (Timed timed in pending.Values)
            {
                timed.Up = stopTime;
                timed.Step.HoldMs = Clamp(Elapsed(timed.Down, timed.Up), 1, Step.MaxHoldMs);
                result.Add(timed);
            }

            result.Sort((a, b) => Elapsed(b.Down, a.Down));

            var steps = new List<Step>(result.Count);
            for (int i = 0; i < result.Count; i++)
            {
                Step step = result[i].Step;
                step.DelayAfterMs = i + 1 < result.Count
                    ? Clamp(Elapsed(result[i].Up, result[i + 1].Down), 0, Step.MaxDelayAfterMs)
                    : 0;
                steps.Add(step);
            }

            return steps;
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private sealed class Timed
        {
            public Step Step;
            public uint Down;
            public uint Up;
        }
    }
}
