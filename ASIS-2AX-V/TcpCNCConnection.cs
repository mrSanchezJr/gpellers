using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace G_write
{
    /// <summary>
    /// Реализация подключения к контроллеру через TCP (Wi-Fi ESP-32S мост).
    /// </summary>
    public class TcpCNCConnection : ICNCConnection
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private readonly object _writeLock = new object();
        private bool _isDisposed;

        public event EventHandler<string>? LineReceived;
        public event EventHandler? Disconnected;

        public bool IsOpen => _client != null && _client.Connected && _stream != null;

        public TcpCNCConnection(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void Open()
        {
            if (IsOpen) return;

            _client = new TcpClient
            {
                NoDelay = true, // Отключаем алгоритм Нагла для мгновенной передачи пакетов
                SendTimeout = 3000,
                ReceiveTimeout = 0
            };

            // Подключение с таймаутом 4 секунды (чтобы не подвешивать UI)
            var connectTask = _client.ConnectAsync(_host, _port);
            if (!connectTask.Wait(TimeSpan.FromSeconds(4)))
            {
                _client.Close();
                _client = null;
                throw new TimeoutException($"Не удалось подключиться к {_host}:{_port} (тайм-аут ожидания).");
            }

            _stream = _client.GetStream();
            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            if (_stream == null) return;

            byte[] buffer = new byte[2048];
            var lineBuilder = new StringBuilder();

            try
            {
                while (!cancellationToken.IsCancellationRequested && IsOpen)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0)
                    {
                        // Удаленный сервер закрыл соединение
                        break;
                    }

                    for (int i = 0; i < bytesRead; i++)
                    {
                        char c = (char)buffer[i];
                        if (c == '\n')
                        {
                            string line = lineBuilder.ToString().TrimEnd('\r');
                            lineBuilder.Clear();
                            if (line.Length > 0)
                            {
                                LineReceived?.Invoke(this, line);
                            }
                        }
                        else
                        {
                            lineBuilder.Append(c);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // Ошибка сети или разрыв
            }
            finally
            {
                if (!_isDisposed)
                {
                    Disconnected?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void Write(string text)
        {
            if (!IsOpen || _stream == null) return;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                lock (_writeLock)
                {
                    _stream.Write(bytes, 0, bytes.Length);
                    _stream.Flush();
                }
            }
            catch
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }

        public void WriteLine(string text)
        {
            if (!IsOpen || _stream == null) return;
            try
            {
                if (!text.EndsWith("\n"))
                {
                    text += "\n";
                }
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                lock (_writeLock)
                {
                    _stream.Write(bytes, 0, bytes.Length);
                    _stream.Flush();
                }
            }
            catch
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (!IsOpen || _stream == null) return;
            try
            {
                lock (_writeLock)
                {
                    _stream.Write(buffer, offset, count);
                    _stream.Flush();
                }
            }
            catch
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }

        public void Close()
        {
            try
            {
                _cts?.Cancel();
                _stream?.Close();
                _client?.Close();
                _stream = null;
                _client = null;
            }
            catch { }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                Close();
                _cts?.Dispose();
            }
        }
    }
}
