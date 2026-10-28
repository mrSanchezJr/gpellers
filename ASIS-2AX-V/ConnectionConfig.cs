using System;
using System.IO;
using System.Text.Json;

namespace G_write
{
    public class ConnectionConfig
    {
        public bool IsTcpMode { get; set; } = true;
        public string TcpHost { get; set; } = "192.168.1.150";
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
                    var cfg = JsonSerializer.Deserialize<ConnectionConfig>(json);
                    if (cfg != null) return cfg;
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
}
