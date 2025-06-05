using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using UIAutomationClient = Interop.UIAutomationClient;


namespace CLIENT.Helpers
{
    public static class MouseDetector
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;

        private static IntPtr _mouseHookID = IntPtr.Zero;
        private static LowLevelMouseProc _mouseProc;

        public delegate void MouseHoverHandler(int x, int y);
        public static event MouseHoverHandler OnMouseHover;

        public delegate void MouseHoverReadHandler(string content);
        public static event MouseHoverReadHandler OnMouseHoverRead;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private static Timer _hoverTimer = new Timer(200); // 200ms delay
        private static int _lastX, _lastY;

        static MouseDetector()
        {
            _hoverTimer.AutoReset = false; // Only trigger once per mouse stop
            _hoverTimer.Elapsed += (s, e) =>
            {
                string content = GetContentAtMousePosition(_lastX, _lastY);
                OnMouseHoverRead?.Invoke(content);
            };
        }

        public static void Start()
        {
            if (_mouseHookID == IntPtr.Zero)
            {
                _mouseProc = MouseHookCallback;
                using (var process = Process.GetCurrentProcess())
                using (var module = process.MainModule)
                {
                    _mouseHookID = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(module.ModuleName), 0);
                }
            }
            _hoverTimer.Start();
        }

        public static void Stop()
        {
            if (_mouseHookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookID);
                _mouseHookID = IntPtr.Zero;
            }
            _hoverTimer.Stop();
        }

        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
            {
                MSLLHOOKSTRUCT mouseStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int x = mouseStruct.pt.x;
                int y = mouseStruct.pt.y;

                // Trigger immediate hover event
                OnMouseHover?.Invoke(x, y);

                // Only reset timer if position changed
                if (_lastX != x || _lastY != y)
                {
                    _lastX = x;
                    _lastY = y;
                    _hoverTimer.Stop();
                    _hoverTimer.Start();
                }
            }

            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }
        private static string GetContentAtMousePosition(int x, int y)
        {
            try
            {
                // Use user32 POINT for WindowFromPoint
                POINT point = new POINT { x = x, y = y };
                IntPtr hWnd = WindowFromPoint(point);

                if (hWnd != IntPtr.Zero)
                {
                    // Initialize the UI Automation client
                    var automation = new UIAutomationClient.CUIAutomation();

                    // Use UIAutomationClient.tagPOINT for ElementFromPoint
                    var uiPoint = new UIAutomationClient.tagPOINT { x = x, y = y };
                    UIAutomationClient.IUIAutomationElement element = automation.ElementFromPoint(uiPoint);

                    if (element != null)
                    {
                        // Retrieve the name property of the element
                        string elementName = element.CurrentName;
                        string controlType = ControlTypeIdToString(element.CurrentControlType);

                        return $"Element Name: {elementName}, Control Type: {controlType}";
                    }

                    return "No UI Automation element at mouse position";
                }

                return "No window at mouse position";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetContentAtMousePosition: {ex.Message}");
                return "Error retrieving content";
            }
        }
        private static string ControlTypeIdToString(int controlTypeId)
        {
            return controlTypeId switch
            {
                50000 => "Button",
                50001 => "Calendar",
                50002 => "CheckBox",
                50003 => "ComboBox",
                50004 => "Edit",
                50005 => "Hyperlink",
                50006 => "Image",
                50007 => "ListItem",
                50008 => "List",
                50009 => "Menu",
                50010 => "MenuBar",
                50011 => "MenuItem",
                50012 => "ProgressBar",
                50013 => "RadioButton",
                50014 => "ScrollBar",
                50015 => "Slider",
                50016 => "Spinner",
                50017 => "StatusBar",
                50018 => "Tab",
                50019 => "TabItem",
                50020 => "Text",
                50021 => "ToolBar",
                50022 => "ToolTip",
                50023 => "Tree",
                50024 => "TreeItem",
                50025 => "Custom",
                50026 => "Group",
                50027 => "Thumb",
                50028 => "DataGrid",
                50029 => "DataItem",
                50030 => "Document",
                50031 => "SplitButton",
                50032 => "Window",
                50033 => "Pane",
                50034 => "Header",
                50035 => "HeaderItem",
                50036 => "Table",
                50037 => "TitleBar",
                50038 => "Separator",
                _ => $"Unknown ControlType ({controlTypeId})",
            };
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }
  
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }
    }
}
