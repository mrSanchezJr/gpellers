using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gpellers;

/// <summary>
/// Универсальный интерфейс подключения к ЧПУ контроллеру (COM-порт или TCP Wi-Fi).
/// Позволяет остальному коду отправлять команды одинаково вне зависимости от типа подключения.
/// </summary>
public interface ICNCConnection : IDisposable
{
    bool IsOpen { get; }
    void Open();
    void Close();
    void Write(string data);
    void WriteLine(string text);
    void Write(byte[] buffer, int offset, int count);
    string ReadLine();
    event Action<string>? OnLineReceived;
    event Action<bool>? OnConnectionStatusChanged;
}

/// <summary>
/// Реализация подключения через последовательный порт (COM в Windows или /dev/ttyUSB* в Linux).
/// </summary>
public class SerialCNCConnection : ICNCConnection
{
    private SerialPort? _serialPort;
    private readonly string _portName;
    private readonly int _baudRate;
    private Thread? _readThread;
    private volatile bool _isRunning;

    public bool IsOpen => _serialPort != null && _serialPort.IsOpen;
    public event Action<string>? OnLineReceived;
    public event Action<bool>? OnConnectionStatusChanged;

    public SerialCNCConnection(string portName, int baudRate = 115200)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    public void Open()
    {
        if (IsOpen) return;

        _serialPort = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 500,
            WriteTimeout = 500,
            NewLine = "\n",
            DtrEnable = true,
            RtsEnable = true
        };

        _serialPort.Open();
        _isRunning = true;
        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "SerialReader"
        };
        _readThread.Start();

        OnConnectionStatusChanged?.Invoke(true);
    }

    public void Close()
    {
        _isRunning = false;
        try
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }
        catch { }
        finally
        {
            _serialPort?.Dispose();
            _serialPort = null;
            OnConnectionStatusChanged?.Invoke(false);
        }
    }

    public void Write(string data)
    {
        if (!IsOpen || _serialPort == null) return;
        try
        {
            _serialPort.Write(data);
        }
        catch { }
    }

    public void WriteLine(string text)
    {
        if (!IsOpen || _serialPort == null) return;
        try
        {
            _serialPort.WriteLine(text);
        }
        catch { }
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        if (!IsOpen || _serialPort == null) return;
        try
        {
            _serialPort.Write(buffer, offset, count);
        }
        catch { }
    }

    public string ReadLine()
    {
        if (!IsOpen || _serialPort == null) return string.Empty;
        try
        {
            return _serialPort.ReadLine();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ReadLoop()
    {
        var sb = new StringBuilder();
        while (_isRunning && _serialPort != null && _serialPort.IsOpen)
        {
            try
            {
                int b = _serialPort.ReadByte();
                if (b == -1) continue;

                char c = (char)b;
                if (c == '\n')
                {
                    string line = sb.ToString().TrimEnd('\r');
                    sb.Clear();
                    if (!string.IsNullOrEmpty(line))
                    {
                        OnLineReceived?.Invoke(line);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            catch (TimeoutException) { }
            catch (Exception)
            {
                if (_isRunning)
                {
                    Close();
                }
                break;
            }
        }
    }

    public static string[] GetAvailablePorts()
    {
        var ports = new List<string>();

        // Стандартные порты Windows (COM1, COM2...)
        try
        {
            ports.AddRange(SerialPort.GetPortNames());
        }
        catch { }

        // Поиск портов в Linux (/dev/ttyUSB*, /dev/ttyACM*)
        if (Environment.OSVersion.Platform == PlatformID.Unix ||
            Environment.OSVersion.Platform == PlatformID.Other)
        {
            try
            {
                if (Directory.Exists("/dev"))
                {
                    foreach (var dev in Directory.GetFiles("/dev"))
                    {
                        if (dev.StartsWith("/dev/ttyUSB") || dev.StartsWith("/dev/ttyACM"))
                        {
                            if (!ports.Contains(dev))
                                ports.Add(dev);
                        }
                    }
                }
            }
            catch { }
        }

        return ports.ToArray();
    }

    public void Dispose()
    {
        Close();
    }
}

/// <summary>
/// Реализация подключения через Wi-Fi TCP (сокет к контроллеру Inectra / ESP32).
/// </summary>
public class TcpCNCConnection : ICNCConnection
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly string _host;
    private readonly int _port;
    private Thread? _readThread;
    private volatile bool _isRunning;
    private readonly object _writeLock = new object();

    public bool IsOpen => _tcpClient != null && _tcpClient.Connected;
    public event Action<string>? OnLineReceived;
    public event Action<bool>? OnConnectionStatusChanged;

    public TcpCNCConnection(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public void Open()
    {
        if (IsOpen) return;

        _tcpClient = new TcpClient();
        // Подключение с таймаутом 3 секунды
        var result = _tcpClient.BeginConnect(_host, _port, null, null);
        bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));

        if (!success || !_tcpClient.Connected)
        {
            _tcpClient.Close();
            _tcpClient = null;
            throw new TimeoutException($"Не удалось подключиться к TCP {_host}:{_port}");
        }

        _tcpClient.EndConnect(result);
        _tcpClient.NoDelay = true; // Отключаем алгоритм Nagle для минимальной задержки
        _stream = _tcpClient.GetStream();
        _isRunning = true;

        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "TcpReader"
        };
        _readThread.Start();

        OnConnectionStatusChanged?.Invoke(true);
    }

    public void Close()
    {
        _isRunning = false;
        try
        {
            _stream?.Close();
            _stream?.Dispose();
        }
        catch { }
        try
        {
            _tcpClient?.Close();
            _tcpClient?.Dispose();
        }
        catch { }
        finally
        {
            _stream = null;
            _tcpClient = null;
            OnConnectionStatusChanged?.Invoke(false);
        }
    }

    public void Write(string data)
    {
        if (!IsOpen || _stream == null) return;
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            lock (_writeLock)
            {
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
        }
        catch
        {
            Close();
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
            Write(text);
        }
        catch
        {
            Close();
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
            Close();
        }
    }

    public string ReadLine()
    {
        return string.Empty;
    }

    private void ReadLoop()
    {
        var sb = new StringBuilder();
        byte[] buffer = new byte[1024];

        while (_isRunning && _stream != null)
        {
            try
            {
                int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break;
                }

                for (int i = 0; i < bytesRead; i++)
                {
                    char c = (char)buffer[i];
                    if (c == '\n')
                    {
                        string line = sb.ToString().TrimEnd('\r');
                        sb.Clear();
                        if (!string.IsNullOrEmpty(line))
                        {
                            OnLineReceived?.Invoke(line);
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }
            catch
            {
                break;
            }
        }

        if (_isRunning)
        {
            Close();
        }
    }

    public void Dispose()
    {
        Close();
    }
}
