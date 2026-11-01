using System;
using System.IO;
using System.Text.Json;

namespace Gpellers;

/// <summary>
/// Конфигурация подключения к контроллеру ЧПУ (COM или TCP).
/// Сохраняется в файл connection_config.json рядом с исполняемым файлом.
/// </summary>
public class ConnectionConfig
{
    public bool IsTcpMode { get; set; } = true;
    public string TcpHost { get; set; } = "192.168.4.1";
    public int TcpPort { get; set; } = 8088;
    public string LastComPort { get; set; } = "";

    private static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connection_config.json");

    public static ConnectionConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<ConnectionConfig>(json) ?? new ConnectionConfig();
            }
        }
        catch { }
        return new ConnectionConfig();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
