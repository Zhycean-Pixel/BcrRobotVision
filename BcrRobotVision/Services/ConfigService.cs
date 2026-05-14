using System;
using System.IO;
using System.Text.Json;
using BcrRobotVision.Models;

namespace BcrRobotVision.Services
{
    public class ConfigService
    {
        private readonly string _configPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "appsettings.json");

        public AppConfig Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    var defaultConfig = new AppConfig();
                    Save(defaultConfig);
                    return defaultConfig;
                }

                string json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);

                return config ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save(AppConfig config)
        {
            string? dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_configPath, json);
        }
    }
}