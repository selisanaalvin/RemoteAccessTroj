using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CLIENT.Helpers
{
    public static class KeyboardDetector
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private static IntPtr _hookID = IntPtr.Zero;
        private static LowLevelKeyboardProc _proc;

        public delegate void KeyPressedHandler(string key);
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
        private static extern int GetKeyNameText(int lParam, [Out] System.Text.StringBuilder lpString, int nSize);

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

                // Convert VK code to string
                string key = ((ConsoleKey)vkCode).ToString();

                OnKeyPressed?.Invoke(key);
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
    }
}
