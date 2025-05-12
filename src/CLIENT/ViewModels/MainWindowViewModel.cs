using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;

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

        public MainWindowViewModel()
        {
            ConnectToServer();
        }

        private void ConnectToServer()
        {
            Task.Run(() =>
            {
                try
                {
                    using var client = new TcpClient();
                    client.Connect("127.0.0.1", 5000);

                    var stream = client.GetStream();

                    // Send client's IP or machine name
                    string clientMessage = $"{Environment.MachineName}";
                    byte[] sendBuffer = Encoding.UTF8.GetBytes(clientMessage);
                    stream.Write(sendBuffer, 0, sendBuffer.Length);

                    // Read response from server
                    byte[] readBuffer = new byte[1024];
                    int length = stream.Read(readBuffer, 0, readBuffer.Length);
                    string serverMessage = Encoding.UTF8.GetString(readBuffer, 0, length);

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Greeting = "Server says: " + serverMessage;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Greeting = "Connection failed: " + ex.Message;
                    });
                }
            });
        }

    }
}
