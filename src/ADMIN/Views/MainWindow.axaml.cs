using Avalonia.Controls;
using ADMIN.ViewModels;
using System.Threading.Tasks;
using System;

namespace ADMIN.Views;
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    // Seed FilteredClients with all items on startup
                    vm.SyncFilteredClients();
                }
            };
        }

        private void SendMessage(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Get the message text from the TextBox
            var messageTextBox = this.FindControl<TextBox>("Command");
            var message = messageTextBox.Text;

            var targetIp = this.FindControl<TextBox>("IPTarget");
            var ipTar = targetIp.Text;

            if (!string.IsNullOrWhiteSpace(message))
            {
                var viewModel = (MainWindowViewModel)DataContext;
                viewModel.SendMessageAsync(ipTar, message);
            }
        }

        private void ExportLog(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;
            viewModel.ExportTxt();
        }

    // ── Client list: click to set target IP and auto-filter activity log ────
    private void ClientList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is string ip)
        {
            var ipBox = this.FindControl<TextBox>("IPTarget");
            if (ipBox != null)
                ipBox.Text = ip;

            var viewModel = (MainWindowViewModel)DataContext;
            // Auto-filter activity log to the selected IP
            viewModel.ForceApplyFilter(ip);

            // Deselect so the item can be clicked again later
            lb.SelectedItem = null;
        }
    }

    // ── View Desktop (new) ───────────────────────────────────────────────────
    private async void ViewDesktop(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetIp = this.FindControl<TextBox>("IPTarget");
        string ip = targetIp?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ip)) return;

        var viewModel = (MainWindowViewModel)DataContext;
        var win = new DesktopWindow(viewModel, ip);
        win.Show();
        await viewModel.StartScreenStreamAsync(ip);
    }

    // ── File Manager (new) ───────────────────────────────────────────────────
    private void OpenFileManager(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetIp = this.FindControl<TextBox>("IPTarget");
        string ip = targetIp?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ip)) return;

        var viewModel = (MainWindowViewModel)DataContext;
        var fmVm = viewModel.GetOrCreateFileManager(ip);

        // Wire upload: FileManagerWindow calls UploadSelectedAsync which
        // delegates actual byte-sending to MainWindowViewModel
        // SendCommand is fully wired inside GetOrCreateFileManager — no override needed here.

        var win = new FileManagerWindow(fmVm);
        win.Show();
    }

}