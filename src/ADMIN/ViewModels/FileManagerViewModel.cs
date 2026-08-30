using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ADMIN.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADMIN.ViewModels
{
    public partial class FileManagerViewModel : ObservableObject
    {
        // ── State ────────────────────────────────────────────────────────────
        public ObservableCollection<FileTreeNode> LocalTree  { get; } = new();
        public ObservableCollection<FileTreeNode> RemoteTree { get; } = new();

        [ObservableProperty] private FileTreeNode? _selectedLocalNode;
        [ObservableProperty] private FileTreeNode? _selectedRemoteNode;
        [ObservableProperty] private string _status = "Ready";
        [ObservableProperty] private bool _isTransferring = false;
        [ObservableProperty] private string _transferLabel = string.Empty;

        private readonly string _clientIp;

        // Delegate wired up by MainWindowViewModel so this VM can send commands
        public Func<string, string, Task>? SendCommand { get; set; }

        // Pending remote expand: which node is waiting for a pathlist: response
        private FileTreeNode? _pendingRemoteNode;

        public FileManagerViewModel(string clientIp)
        {
            _clientIp = clientIp;
            LoadLocalRoots();
            LoadRemoteRoots();
        }

        // ── LOCAL tree ───────────────────────────────────────────────────────

        private void LoadLocalRoots()
        {
            LocalTree.Clear();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var node = new FileTreeNode(drive.RootDirectory.FullName, true, isLocal: true);
                LocalTree.Add(node);
            }
        }

        public void ExpandLocalNode(FileTreeNode node)
        {
            if (!node.IsDirectory || node.IsLoaded) return;
            node.IsLoaded = true;
            node.Children.Clear();

            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(node.FullPath))
                {
                    bool isDir = Directory.Exists(entry);
                    node.Children.Add(new FileTreeNode(entry, isDir, isLocal: true));
                }
            }
            catch (Exception ex)
            {
                node.Children.Add(new FileTreeNode($"[Error: {ex.Message}]", false, isLocal: true));
            }
        }

        // ── REMOTE tree ──────────────────────────────────────────────────────

        private void LoadRemoteRoots()
        {
            RemoteTree.Clear();
            // Start at C:\ — same default as existing SendFilePath in CLIENT
            var root = new FileTreeNode(@"C:\", true, isLocal: false);
            RemoteTree.Add(root);
        }

        public async Task ExpandRemoteNodeAsync(FileTreeNode node)
        {
            if (!node.IsDirectory || node.IsLoaded) return;
            node.IsLoaded = true;
            _pendingRemoteNode = node;
            Status = $"Loading {node.FullPath}...";

            if (SendCommand != null)
                await SendCommand(_clientIp, $"__open_file:{node.FullPath}");
        }

        /// <summary>
        /// Called by MainWindowViewModel when a pathlist: response arrives for this client.
        /// </summary>
        public void HandlePathList(string rawEntries)
        {
            if (_pendingRemoteNode == null) return;

            var node = _pendingRemoteNode;
            _pendingRemoteNode = null;

            Dispatcher.UIThread.Post(() =>
            {
                node.Children.Clear();
                var lines = rawEntries.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string entry = line.Trim();
                    if (string.IsNullOrEmpty(entry)) continue;

                    // Heuristic: entries ending with \ are directories;
                    // also check if the name has no extension as a fallback.
                    bool isDir = entry.EndsWith("\\") || entry.EndsWith("/")
                                 || !Path.HasExtension(entry);
                    node.Children.Add(new FileTreeNode(entry, isDir, isLocal: false));
                }

                if (node.Children.Count == 0)
                    node.Children.Add(new FileTreeNode("[Empty]", false, isLocal: false));

                Status = $"Loaded {node.FullPath} — {lines.Length} entries";
            });
        }

        /// <summary>
        /// Called by MainWindowViewModel when a fileDownload: transfer completes.
        /// Returns the local folder where the file should be saved (selected local folder).
        /// </summary>
        public string GetDownloadDestinationFolder() => SelectedLocalFolder;

        /// <summary>
        /// Called by MainWindowViewModel when a fileDownload: transfer completes.
        /// </summary>
        public void HandleDownloadComplete(string fileName, string savedPath)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsTransferring = false;
                Status = $"Downloaded '{fileName}' → {savedPath}";
                RefreshLocalDownloadFolder(savedPath);
            });
        }

        private void RefreshLocalDownloadFolder(string savedPath)
        {
            string? folder = Path.GetDirectoryName(savedPath);
            if (folder == null) return;

            // Find the local node matching this folder and reload it
            foreach (var root in LocalTree)
                RefreshNodeIfMatch(root, folder);
        }

        private void RefreshNodeIfMatch(FileTreeNode node, string folder)
        {
            if (string.Equals(node.FullPath.TrimEnd('\\', '/'),
                              folder.TrimEnd('\\', '/'),
                              StringComparison.OrdinalIgnoreCase))
            {
                node.IsLoaded = false;
                ExpandLocalNode(node);
                return;
            }
            foreach (var child in node.Children)
                RefreshNodeIfMatch(child, folder);
        }

        // ── Refresh ──────────────────────────────────────────────────────────

        public void RefreshLocal()
        {
            var selected = SelectedLocalNode;
            LoadLocalRoots();

            // Re-expand to the previously selected folder if possible
            if (selected != null)
            {
                foreach (var root in LocalTree)
                    TryExpandToPath(root, selected.FullPath);
            }

            Status = "Local tree refreshed.";
        }

        public async Task RefreshRemoteAsync()
        {
            // Re-expand the currently selected remote node, or reload roots
            if (SelectedRemoteNode is { IsDirectory: true } node)
            {
                node.IsLoaded = false;
                node.Children.Clear();
                await ExpandRemoteNodeAsync(node);
            }
            else
            {
                LoadRemoteRoots();
                Status = "Remote tree refreshed.";
            }
        }

        private void TryExpandToPath(FileTreeNode node, string targetPath)
        {
            if (string.Equals(node.FullPath.TrimEnd('\\', '/'),
                              targetPath.TrimEnd('\\', '/'),
                              StringComparison.OrdinalIgnoreCase))
            {
                node.IsLoaded = false;
                ExpandLocalNode(node);
                return;
            }
            if (targetPath.StartsWith(node.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!node.IsLoaded) ExpandLocalNode(node);
                foreach (var child in node.Children)
                    TryExpandToPath(child, targetPath);
            }
        }

        // ── Commands ─────────────────────────────────────────────────────────

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the folder path of the currently selected LOCAL node.
        /// If a file is selected, returns its parent directory.
        /// Falls back to the user's Desktop.
        /// </summary>
        public string SelectedLocalFolder
        {
            get
            {
                if (SelectedLocalNode == null)
                    return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                return SelectedLocalNode.IsDirectory
                    ? SelectedLocalNode.FullPath
                    : Path.GetDirectoryName(SelectedLocalNode.FullPath)
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
        }

        /// <summary>
        /// Returns the folder path of the currently selected REMOTE node.
        /// If a file is selected, returns its parent directory.
        /// Falls back to C:\.
        /// </summary>
        public string SelectedRemoteFolder
        {
            get
            {
                if (SelectedRemoteNode == null) return @"C:\";
                return SelectedRemoteNode.IsDirectory
                    ? SelectedRemoteNode.FullPath
                    : Path.GetDirectoryName(SelectedRemoteNode.FullPath) ?? @"C:\";
            }
        }

        // ── Commands ─────────────────────────────────────────────────────────

        [RelayCommand]
        public async Task DownloadSelectedAsync()
        {
            if (SelectedRemoteNode == null || SelectedRemoteNode.IsDirectory)
            {
                Status = "Select a file on the REMOTE side to download.";
                return;
            }

            string fileName = Path.GetFileName(SelectedRemoteNode.FullPath);
            TransferLabel = $"⬇  Downloading...";
            Status = $"{fileName}";
            IsTransferring = true;

            try
            {
                if (SendCommand != null)
                    await SendCommand(_clientIp, $"__download_file:{SelectedRemoteNode.FullPath}");
                // IsTransferring is cleared in HandleDownloadComplete
            }
            catch (Exception ex)
            {
                IsTransferring = false;
                Status = $"Download error: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task UploadSelectedAsync(string localFilePath)
        {
            if (!File.Exists(localFilePath))
            {
                Status = "Select a file on the LOCAL side to upload.";
                return;
            }

            try
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(localFilePath);
                string fileName = Path.GetFileName(localFilePath);
                string destFolder = SelectedRemoteFolder;

                TransferLabel = $"⬆  Uploading...";
                Status = $"{fileName}  ({fileBytes.Length / 1024} KB) → {destFolder}";
                IsTransferring = true;

                if (SendCommand != null)
                    await SendCommand(_clientIp, $"__upload_file_raw:{destFolder}:{fileName}:{fileBytes.Length}");

                // Upload is fire-and-forget on the network side; mark done after send
                IsTransferring = false;
                Status = $"Uploaded '{fileName}' → {destFolder}";
            }
            catch (Exception ex)
            {
                IsTransferring = false;
                Status = $"Upload error: {ex.Message}";
            }
        }
    }
}
