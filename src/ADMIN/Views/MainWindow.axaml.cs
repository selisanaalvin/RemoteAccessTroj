using Avalonia.Controls;
using ADMIN.ViewModels;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

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
            // Ensure the method is async and TopLevel is valid
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null || topLevel.StorageProvider == null)
                return;

            // Start async operation to open the dialog
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Text File",
                AllowMultiple = false
            });

            if (files != null && files.Count >= 1)
            {
                // Open reading stream from the first file
                await using var stream = await files[0].OpenReadAsync();
                using var streamReader = new StreamReader(stream);

                // Reads all the content of the file as text
                var fileContent = await streamReader.ReadToEndAsync();

                // Optional: Use the content (e.g., display it in a TextBox)
                var outputTextBox = this.FindControl<TextBox>("Output");
                if (outputTextBox != null)
                {
                    outputTextBox.Text = files[0].Path.LocalPath;
                }
            }
        }
    }