using System;
using System.Runtime.InteropServices;

namespace CLIENT.Helpers
{
    public static class RemoteInput
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(4)] public MOUSEINPUT mi;
            [FieldOffset(4)] public KEYBDINPUT ki;
        }

        /// <summary>
        /// payload format: "xPct,yPct,btn"
        ///   btn = "left" | "right" → move + click
        ///   btn = "move"           → move only (no click)
        /// </summary>
        public static void ReplayMouse(string payload)
        {
            try
            {
                string[] parts = payload.Split(',');
                if (parts.Length < 3) return;

                if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double xPct)) return;
                if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double yPct)) return;

                string btn = parts[2].Trim().ToLower();

                // SendInput absolute coords are 0-65535 mapped to screen
                int absX = (int)(xPct * 65535);
                int absY = (int)(yPct * 65535);

                if (btn == "move")
                {
                    // Move only — no click
                    var moveOnly = new INPUT[]
                    {
                        new INPUT
                        {
                            type = INPUT_MOUSE,
                            mi = new MOUSEINPUT
                            {
                                dx = absX, dy = absY,
                                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE
                            }
                        }
                    };
                    SendInput((uint)moveOnly.Length, moveOnly, Marshal.SizeOf(typeof(INPUT)));
                    return;
                }

                uint downFlag = btn == "right" ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
                uint upFlag   = btn == "right" ? MOUSEEVENTF_RIGHTUP   : MOUSEEVENTF_LEFTUP;

                var inputs = new INPUT[]
                {
                    // Move to position
                    new INPUT
                    {
                        type = INPUT_MOUSE,
                        mi = new MOUSEINPUT
                        {
                            dx = absX, dy = absY,
                            dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE
                        }
                    },
                    // Button down
                    new INPUT
                    {
                        type = INPUT_MOUSE,
                        mi = new MOUSEINPUT { dx = absX, dy = absY, dwFlags = downFlag | MOUSEEVENTF_ABSOLUTE }
                    },
                    // Button up
                    new INPUT
                    {
                        type = INPUT_MOUSE,
                        mi = new MOUSEINPUT { dx = absX, dy = absY, dwFlags = upFlag | MOUSEEVENTF_ABSOLUTE }
                    }
                };

                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            }
            catch { }
        }

        /// <summary>
        /// payload format: virtual key code as string, e.g. "65" for 'A'
        /// </summary>
        public static void ReplayKey(string payload)
        {
            try
            {
                if (!ushort.TryParse(payload.Trim(), out ushort vk)) return;

                var inputs = new INPUT[]
                {
                    new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYDOWN }
                    },
                    new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP }
                    }
                };

                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            }
            catch { }
        }
    }
}
