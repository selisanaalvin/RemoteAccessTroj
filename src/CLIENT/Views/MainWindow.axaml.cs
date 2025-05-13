using Avalonia.Controls;
using CLIENT.Helpers;
using CLIENT.ViewModels;
using System;
using Tmds.DBus.Protocol;

namespace CLIENT.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            GlobalMouseHook.OnGlobalMouseClick += () =>
            {
                string info = WindowDetector.GetActiveWindowInfo();
                var viewModel = (MainWindowViewModel)DataContext;
                viewModel.SendAppInfoAsync(info);
            };

            GlobalMouseHook.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            GlobalMouseHook.Stop();
        }
    }
}
