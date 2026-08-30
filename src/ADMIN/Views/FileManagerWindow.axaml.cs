using Avalonia.Controls;
using ADMIN.Models;
using ADMIN.ViewModels;
using System.IO;
using Microsoft.Win32;

namespace ADMIN.Views
{
    public partial class FileManagerWindow : Window
    {
        private FileManagerViewModel _vm = null!;

        public FileManagerWindow()
        {
            InitializeComponent();
        }

        public FileManagerWindow(FileManagerViewModel vm) : this()
        {
            _vm = vm;
            DataContext = vm;
        }

        // ── Local tree expand on selection ───────────────────────────────────
        private void LocalTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            if (_vm.SelectedLocalNode is { IsDirectory: true } node && !node.IsLoaded)
                _vm.ExpandLocalNode(node);
        }

        // ── Remote tree expand on selection ──────────────────────────────────
        private async void RemoteTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            if (_vm.SelectedRemoteNode is { IsDirectory: true } node && !node.IsLoaded)
                await _vm.ExpandRemoteNodeAsync(node);
        }

        // ── Upload: local selected file → client ─────────────────────────────
        private async void Upload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm.SelectedLocalNode == null || _vm.SelectedLocalNode.IsDirectory)
            {
                _vm.Status = "Select a file on the LOCAL side first.";
                return;
            }
            await _vm.UploadSelectedAsync(_vm.SelectedLocalNode.FullPath);
        }

        // ── Download: remote selected file → admin machine ───────────────────
        private async void Download_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm.SelectedRemoteNode == null || _vm.SelectedRemoteNode.IsDirectory)
            {
                _vm.Status = "Select a file on the REMOTE side first.";
                return;
            }
            await _vm.DownloadSelectedAsync();
        }

        // ── Refresh LOCAL pane ────────────────────────────────────────────────
        private void RefreshLocal_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm == null) return;
            _vm.RefreshLocal();
        }

        // ── Refresh REMOTE pane ───────────────────────────────────────────────
        private async void RefreshRemote_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.RefreshRemoteAsync();
        }

        // ── Attach a local file via dialog then upload it to the client ───────
        private async void AttachAndUpload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select a file to upload to client",
                AllowMultiple = false
            };

            var result = await dialog.ShowAsync(this);
            if (result == null || result.Length == 0)
            {
                _vm.Status = "No file selected.";
                return;
            }

            string localPath = result[0];
            _vm.Status = $"Uploading '{Path.GetFileName(localPath)}'...";
            await _vm.UploadSelectedAsync(localPath);
        }
    }
}
