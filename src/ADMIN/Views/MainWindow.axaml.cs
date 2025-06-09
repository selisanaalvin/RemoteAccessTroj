using Avalonia.Controls;
using ADMIN.ViewModels;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Tmds.DBus.Protocol;

namespace ADMIN.Views;
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
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

        private async void FileDialog(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
        var targetIp = this.FindControl<TextBox>("IPTarget");
        var path = this.FindControl<TextBox>("Path");
        var viewModel = (MainWindowViewModel)DataContext;
        viewModel.ViewDirectories(targetIp.Text, $"{Path.Text}");
    }
    }