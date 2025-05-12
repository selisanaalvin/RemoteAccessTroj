using System;
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


namespace ADMIN.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<string> ConnectedClients { get; set; } = new ObservableCollection<string>();
        public MainWindowViewModel()
        {
            StartServer();
        }

        // Function to start the server and accept incoming clients
        private void StartServer()
        {
            Task.Run(() =>
            {
                try
                {
                    TcpListener listener = new TcpListener(IPAddress.Any, 5000);
                    listener.Start();

                    while (true)
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint)?.Address.ToString();

                        var stream = client.GetStream();
                        // --- Receive message from client ---
                        byte[] buffer = new byte[1024];
                        int length = stream.Read(buffer, 0, buffer.Length);
                        if (length > 0)
                        {
                            string clientMessage = Encoding.UTF8.GetString(buffer, 0, length);

                            // --- Display or store the client message with IP ---
                            Dispatcher.UIThread.Post(() =>
                            {
                                string entry = $"{clientIp}: {clientMessage}";
                                if (!ConnectedClients.Contains(entry))
                                    ConnectedClients.Add(entry);
                            });

                            // --- Send message to the client ---
                            var serverMessage = $"Hello {clientMessage}";
                            var bytes = Encoding.UTF8.GetBytes(serverMessage);
                            stream.Write(bytes, 0, bytes.Length);
                        }

                        client.Close();
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        ConnectedClients.Add($"Error: {ex.Message}");
                    });
                }
            });
        }

        public void SendMessage(String IpAddress, String Message)
        {
            try { 
            TcpClient client = new TcpClient(IpAddress, 5000);
            var stream = client.GetStream();
            byte[] messageBytes = Encoding.UTF8.GetBytes(Message);
            stream.Write(messageBytes, 0, messageBytes.Length);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ConnectedClients.Add($"Error: {ex.Message}");
                });
            }

        }
    }
}
