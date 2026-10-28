using System;

namespace G_write
{
    /// <summary>
    /// Универсальный интерфейс подключения к ЧПУ контроллеру (COM-порт или TCP Wi-Fi).
    /// </summary>
    public interface ICNCConnection : IDisposable
    {
        bool IsOpen { get; }
        void Open();
        void Close();
        void Write(string text);
        void WriteLine(string text);
        void Write(byte[] buffer, int offset, int count);
        event EventHandler<string>? LineReceived;
        event EventHandler? Disconnected;
    }
}
