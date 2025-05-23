﻿using System;
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

namespace ADMIN.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<string> ConnectedClients { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> ServerLogs { get; set; } = new ObservableCollection<string>();
        
        private ConcurrentDictionary<string, TcpClient> _clientConnections = new();

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
                    _ = HandleClientAsync(client); // fire-and-forget
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ConnectedClients.Add($"Error: {ex.Message}");
                });
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
                        if (!ConnectedClients.Contains(entry))
                        {
                            // Add the entry to the top of the collection
                            ConnectedClients.Insert(0, $"[{timestamp}] {entry}");

                           
                        }
                        ReverseCollection();
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ConnectedClients.Add($"Client Error: {ex.Message}");
                });
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
           ConnectedClients.Reverse().ToList();
         
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
                        ConnectedClients.Insert(0, $"Sent to {ipAddress}: {message}");
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ConnectedClients.Add($"Send failed to {ipAddress}: {ex.Message}");
                    });
                }
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ConnectedClients.Add($"Client {ipAddress} not found.");
                });
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
                ServerLogs.Add($"[SUCCESS] Log file path: {filePath}");
                ReverseCollection();

            }
            catch (IOException ex)
            {
                // Handle any exceptions (e.g., file write errors)
                Console.WriteLine($"Error exporting log: {ex.Message}");
                ServerLogs.Add($"[FAILED] Export Failed: {ex.Message}");
            }

        }
    }
}
