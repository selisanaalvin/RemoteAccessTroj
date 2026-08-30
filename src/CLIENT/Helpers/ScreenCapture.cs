using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CLIENT.Helpers
{
    public static class ScreenCapture
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest,
            int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll")]
        private static extern bool DrawIcon(IntPtr hDC, int x, int y, IntPtr hIcon);

        private const int SRCCOPY = 0x00CC0020;
        private const int CURSOR_SHOWING = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        private static CancellationTokenSource? _cts;
        private static readonly object _lock = new();

        public static int ScreenWidth { get; private set; }
        public static int ScreenHeight { get; private set; }

        public static void Start(NetworkStream stream, int fps = 10)
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
            }

            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                int delay = 1000 / Math.Max(1, fps);
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        byte[] jpeg = CaptureScreen();
                        if (jpeg.Length == 0) { await Task.Delay(delay, token); continue; }

                        // header: frame:<W>x<H>:<size>\n
                        string header = $"frame:{ScreenWidth}x{ScreenHeight}:{jpeg.Length}\n";
                        byte[] headerBytes = Encoding.UTF8.GetBytes(header);

                        await stream.WriteAsync(headerBytes, 0, headerBytes.Length, token);
                        await stream.WriteAsync(jpeg, 0, jpeg.Length, token);
                        await stream.FlushAsync(token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { break; }

                    await Task.Delay(delay, token);
                }
            }, token);
        }

        public static void Stop()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts = null;
            }
        }

        private static byte[] CaptureScreen()
        {
            IntPtr desktopWnd = GetDesktopWindow();
            IntPtr desktopDC = GetDC(desktopWnd);
            IntPtr memDC = CreateCompatibleDC(desktopDC);

            // Use GetSystemMetrics to get primary screen dimensions (no WinForms dependency)
            int width = GetSystemMetrics(0);   // SM_CXSCREEN
            int height = GetSystemMetrics(1);  // SM_CYSCREEN
            if (width <= 0) width = 1920;
            if (height <= 0) height = 1080;

            ScreenWidth = width;
            ScreenHeight = height;

            IntPtr hBitmap = CreateCompatibleBitmap(desktopDC, width, height);
            IntPtr oldObj = SelectObject(memDC, hBitmap);

            BitBlt(memDC, 0, 0, width, height, desktopDC, 0, 0, SRCCOPY);

            // Draw cursor onto the captured bitmap
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
            if (GetCursorInfo(ref ci) && ci.flags == CURSOR_SHOWING)
                DrawIcon(memDC, ci.ptScreenPos.x, ci.ptScreenPos.y, ci.hCursor);

            SelectObject(memDC, oldObj);
            DeleteDC(memDC);
            ReleaseDC(desktopWnd, desktopDC);

            using var bmp = Image.FromHbitmap(hBitmap);
            DeleteObject(hBitmap);

            using var ms = new MemoryStream();
            var jpegEncoder = GetJpegEncoder();
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 50L);
            bmp.Save(ms, jpegEncoder, encoderParams);
            return ms.ToArray();
        }

        private static ImageCodecInfo GetJpegEncoder()
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
                if (codec.MimeType == "image/jpeg") return codec;
            throw new InvalidOperationException("JPEG encoder not found.");
        }
    }
}
