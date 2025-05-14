using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CLIENT.Helpers;
using CLIENT.ViewModels;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace CLIENT.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Minimize the window and make it invisible
            this.WindowState = WindowState.Minimized;
            this.ShowInTaskbar = false;  // Ensure it doesn't appear on the taskbar
            this.Closing += MainWindow_Closing;
            ExtendClientAreaToDecorationsHint = true;

            // Run background tasks
            RunBackgroundTasks();

            // Start a task to monitor if the application is killed
            MonitorAppProcess();
        }
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Cancel the closing event
            e.Cancel = true;
        }


        // Override the OnPropertyChanged to prevent WindowState changes
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            // If WindowState changes, force it back to Maximized
            if (e.Property == WindowStateProperty && (WindowState == WindowState.Maximized || WindowState == WindowState.Normal))
            {
                WindowState = WindowState.Minimized;
            }
        }
        private void RunBackgroundTasks()
        {
            // Start the global mouse hook
            GlobalMouseHook.OnGlobalMouseClick += () =>
            {
                string info = WindowDetector.GetActiveWindowInfo();
                var viewModel = (MainWindowViewModel)DataContext;
                viewModel.SendAppInfoAsync(info);
            };

            GlobalMouseHook.Start();
        }

        // Handle cleanup when the window is closed
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            //// Stop global mouse hook and clean up
            GlobalMouseHook.Stop();
        }

        // Task to monitor the application and relaunch it if it is killed
        private async void MonitorAppProcess()
        {
            await Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        // Check if the application is running (you can replace this with the actual process name)
                        var currentProcess = Process.GetCurrentProcess();

                        if (!Process.GetProcessesByName(currentProcess.ProcessName).Any())
                        {
                            // Application process is not running, restart it
                            Process.Start(currentProcess.StartInfo.FileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle any errors if necessary
                        Console.WriteLine($"Error monitoring process: {ex.Message}");
                    }

                    // Wait for a bit before checking again
                    Thread.Sleep(1000);  // Check every second
                }
            });
        }
    }
}
