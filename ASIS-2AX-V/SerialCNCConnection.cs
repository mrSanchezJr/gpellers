using System;
using System.IO.Ports;

namespace G_write
{
    /// <summary>
    /// Реализация подключения к контроллеру через Serial (COM-порт).
    /// </summary>
    public class SerialCNCConnection : ICNCConnection
    {
        private readonly string _portName;
        private readonly int _baudRate;
        private SerialPort? _serialPort;
        private bool _isDisposed;

        public event EventHandler<string>? LineReceived;
        public event EventHandler? Disconnected;

        public bool IsOpen => _serialPort != null && _serialPort.IsOpen;

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
                NewLine = "\n",
                ReadTimeout = 2000,
                WriteTimeout = 2000
            };

            _serialPort.DataReceived += OnDataReceived;
            _serialPort.Open();
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                while (_serialPort.IsOpen && _serialPort.BytesToRead > 0)
                {
                    string line = _serialPort.ReadLine();
                    LineReceived?.Invoke(this, line);
                }
            }
            catch (TimeoutException) { }
            catch (Exception)
            {
                if (!_isDisposed && (_serialPort == null || !_serialPort.IsOpen))
                {
                    Disconnected?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void Write(string text)
        {
            if (!IsOpen || _serialPort == null) return;
            try
            {
                _serialPort.Write(text);
            }
            catch
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }

        public void WriteLine(string text)
        {
            if (!IsOpen || _serialPort == null) return;
            try
            {
                _serialPort.WriteLine(text);
            }
            catch
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (!IsOpen || _serialPort == null) return;
            try
            {
                _serialPort.Write(buffer, offset, count);
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
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= OnDataReceived;
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                Close();
            }
        }
    }
}
