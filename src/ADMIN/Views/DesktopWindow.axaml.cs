using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ADMIN.ViewModels;
using System;
using System.IO;

namespace ADMIN.Views
{
    public partial class DesktopWindow : Window
    {
        private readonly MainWindowViewModel _vm;
        private readonly string _clientIp;

        // Remote screen dimensions reported by CLIENT (used for coordinate mapping)
        private int _remoteW = 1920;
        private int _remoteH = 1080;

        // Whether the stream is currently running
        private bool _streaming = true;

        public DesktopWindow()
        {
            InitializeComponent();
        }

        public DesktopWindow(MainWindowViewModel vm, string clientIp) : this()
        {
            _vm = vm;
            _clientIp = clientIp;

            ClientLabel.Text = $"Remote: {clientIp}";

            // Subscribe to incoming frames for this client
            _vm.FrameReceived += OnFrameReceived;

            // Attach pointer events to the Viewbox — it always fills the content
            // area and reliably receives hit-test events unlike Image inside Viewbox.
            ScreenViewbox.PointerPressed += OnPointerPressed;
            ScreenViewbox.PointerMoved   += OnPointerMoved;

            // Key events on the Window itself
            KeyDown += OnKeyDown;

            // Unsubscribe and stop stream on close
            Closing += (_, _) =>
            {
                _vm.FrameReceived -= OnFrameReceived;
                _ = _vm.StopScreenStreamAsync(_clientIp);
            };
        }

        // ── Incoming frame ───────────────────────────────────────────────────
        private void OnFrameReceived(string ip, byte[] jpegBytes, int remoteW, int remoteH)
        {
            if (ip != _clientIp) return;

            _remoteW = remoteW > 0 ? remoteW : _remoteW;
            _remoteH = remoteH > 0 ? remoteH : _remoteH;

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    using var ms = new MemoryStream(jpegBytes);
                    var bmp = new Bitmap(ms);
                    ScreenImage.Source = bmp;
                    StatusLabel.Text = $"Live — {jpegBytes.Length / 1024} KB/frame  |  Remote: {_remoteW}×{_remoteH}";
                }
                catch { }
            });
        }

        // ── Coordinate helper ────────────────────────────────────────────────
        // Events are attached to ScreenViewbox. GetPosition(ScreenViewbox) gives
        // coords inside the Viewbox area. We then account for Stretch="Uniform"
        // letterbox/pillarbox bars to get the true [0,1] image percentage.
        private bool TryGetPct(PointerEventArgs e, out double xPct, out double yPct)
        {
            xPct = yPct = 0;

            // Viewbox rendered size — use its Bounds (width/height in parent space)
            double vbW = ScreenViewbox.Bounds.Width;
            double vbH = ScreenViewbox.Bounds.Height;
            if (vbW <= 0 || vbH <= 0) return false;

            var pos = e.GetPosition(ScreenViewbox);

            // Compute the actual image rect inside the Viewbox (Stretch=Uniform)
            double imgAspect = (_remoteW > 0 && _remoteH > 0)
                ? (double)_remoteW / _remoteH
                : 16.0 / 9.0;
            double vbAspect = vbW / vbH;

            double imgW, imgH, imgX, imgY;
            if (imgAspect > vbAspect)
            {
                imgW = vbW;
                imgH = vbW / imgAspect;
                imgX = 0;
                imgY = (vbH - imgH) / 2.0;
            }
            else
            {
                imgH = vbH;
                imgW = vbH * imgAspect;
                imgX = (vbW - imgW) / 2.0;
                imgY = 0;
            }

            // Clamp — don't reject, just clamp to image edges
            xPct = Math.Clamp((pos.X - imgX) / imgW, 0, 1);
            yPct = Math.Clamp((pos.Y - imgY) / imgH, 0, 1);
            return true;
        }

        // ── Mouse click → remote input ───────────────────────────────────────
        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!TryGetPct(e, out double xPct, out double yPct)) return;

            // PointerUpdateKind correctly identifies which button was pressed
            var kind = e.GetCurrentPoint(ScreenViewbox).Properties.PointerUpdateKind;
            string btn = kind == PointerUpdateKind.RightButtonPressed ? "right" : "left";

            string payload =
                $"{xPct.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"{yPct.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"{btn}";

            _ = _vm.SendInputEventAsync(_clientIp, $"input:mouse:{payload}");
            e.Handled = true;
        }

        // ── Mouse move → remote input ────────────────────────────────────────
        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!TryGetPct(e, out double xPct, out double yPct)) return;

            string payload =
                $"{xPct.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"{yPct.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                "move";

            _ = _vm.SendInputEventAsync(_clientIp, $"input:mouse:{payload}");
        }

        // ── Key press → remote input ─────────────────────────────────────────
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            int vk = (int)e.Key;
            _ = _vm.SendInputEventAsync(_clientIp, $"input:key:{vk}");
            e.Handled = true;
        }

        // ── Stop / Start toggle button ───────────────────────────────────────
        private async void ToggleStream_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_streaming)
            {
                // Stop the stream
                await _vm.StopScreenStreamAsync(_clientIp);
                _streaming = false;
                StatusLabel.Text = "Stream stopped. Click ▶ Start Stream to resume.";

                // Switch button to green "Start" style
                BtnToggleStream.Content = "▶  Start Stream";
                BtnToggleStream.Classes.Remove("stop-btn");
                BtnToggleStream.Classes.Add("start-btn");
            }
            else
            {
                // Restart the stream
                await _vm.StartScreenStreamAsync(_clientIp);
                _streaming = true;
                StatusLabel.Text = "Connecting...";

                // Switch button back to red "Stop" style
                BtnToggleStream.Content = "⏹  Stop Stream";
                BtnToggleStream.Classes.Remove("start-btn");
                BtnToggleStream.Classes.Add("stop-btn");
            }
        }
    }
}
