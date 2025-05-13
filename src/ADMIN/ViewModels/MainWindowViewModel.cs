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


namespace ADMIN.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<string> ConnectedClients { get; set; } = new ObservableCollection<string>();
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
            try
            {
                string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint)?.Address.ToString();
                NetworkStream stream = client.GetStream();

                byte[] buffer = new byte[1024];
                int length = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (length > 0)
                {
                    string clientMessage = Encoding.UTF8.GetString(buffer, 0, length);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        string entry = $"{clientIp}: {clientMessage}";
                        if (!ConnectedClients.Contains(entry))
                            ConnectedClients.Add(entry);
                    });

                    // Store or update client connection
                    _clientConnections[clientIp] = client;

                    // Respond to client
                    string serverMessage = $"Hello {clientMessage}";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(serverMessage);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    Console.WriteLine(ConnectedClients);
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ConnectedClients.Add($"Client Error: {ex.Message}");
                });
            }
        }

        public async Task SendMessageAsync(string ipAddress, string message)
        {
            if (_clientConnections.TryGetValue(ipAddress, out TcpClient client))
            {
                try
                {
                    var stream = client.GetStream();
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await stream.WriteAsync(messageBytes, 0, messageBytes.Length);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ConnectedClients.Add($"Sent to {ipAddress}: {message}");
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
    }
}
