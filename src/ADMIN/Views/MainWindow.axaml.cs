using Avalonia.Controls;
using ADMIN.ViewModels;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Tmds.DBus.Protocol;
using System;
using System.Net.Sockets;
using System.Windows;
using Microsoft.Win32;

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
        viewModel.ViewDirectories(targetIp.Text, $"{path.Text}");
        }

    private async void DownloadFile(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetIp = this.FindControl<TextBox>("IPTarget");
        var path = this.FindControl<TextBox>("Path");
        var viewModel = (MainWindowViewModel)DataContext;
        viewModel.DownloadFile(targetIp.Text, $"{path.Text}");
    }

    private async void AttachFile(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Title = "Select a File",
            AllowMultiple = false 
        };

        var result = await openFileDialog.ShowAsync(this);

        if (result != null && result.Length > 0)
        {
            string selectedFilePath = result[0];
            Attachment.Text = selectedFilePath;
        }
        else
        {
            // Handle case where no file was selected
            Attachment.Text = "No file selected.";
        }
    }


    private async void UploadFile(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetIp = this.FindControl<TextBox>("IPTarget");
        var attachment = this.FindControl<TextBox>("Attachment");
        var viewModel = (MainWindowViewModel)DataContext;
        viewModel.UploadFile(targetIp.Text, $"{attachment.Text}");
    }


}