using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CLIENT.Helpers
{
    public static class WindowDetector
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public static string GetActiveWindowInfo()
        {
            const int nChars = 256;
            StringBuilder buff = new StringBuilder(nChars);
            IntPtr handle = GetForegroundWindow();

            GetWindowText(handle, buff, nChars);
            GetWindowThreadProcessId(handle, out uint pid);

            string title = buff.ToString();
            string procName = Process.GetProcessById((int)pid).ProcessName;

            return $"App: {procName}, Title: {title}";
        }
    }
}
