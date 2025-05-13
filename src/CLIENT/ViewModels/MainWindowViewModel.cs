using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using DotNetEnv;

namespace CLIENT.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private string _greeting = "Connecting to server...";
        public string Greeting
        {
            get => _greeting;
            set => SetProperty(ref _greeting, value);
        }

        private TcpClient _client;
        private NetworkStream _stream;

        public MainWindowViewModel()
        {
            DotNetEnv.Env.Load();
            _ = ConnectToServerAsync();
        }

        private async Task ConnectToServerAsync()
        {
            try
            {
                _client = new TcpClient();
                string serverPortStr = Environment.GetEnvironmentVariable("SERVER_PORT") ?? "2025";
                int port = int.TryParse(serverPortStr, out int p) ? p : 5000;
                string serverIp = Environment.GetEnvironmentVariable("MASTER_IP") ?? "127.0.0.1";
                await _client.ConnectAsync(serverIp, port);
                _stream = _client.GetStream();

                // Send initial machine name
                string clientMessage = Environment.MachineName;
                byte[] sendBuffer = Encoding.UTF8.GetBytes(clientMessage);
                await _stream.WriteAsync(sendBuffer, 0, sendBuffer.Length);

                // Start reading responses
                _ = ListenToServerAsync();
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Greeting = "Connection failed: " + ex.Message;
                });
            }
        }

        private async Task ListenToServerAsync()
        {
            try
            {
                while (_client != null && _client.Connected)
                {
                    var buffer = new byte[1024];
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break; // Disconnected

                    string serverMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Greeting = "Server says: " + serverMessage;
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Greeting = "Disconnected: " + ex.Message;
                });
            }
        }

        public async Task SendMessageAsync(string message)
        {
            try
            {
                if (_client != null && _client.Connected && _stream != null)
                {
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await _stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Greeting = "Not connected to server.";
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Greeting = "Send failed: " + ex.Message;
                });
            }
        }
    }
}
