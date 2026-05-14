using System;
using System.IO;

namespace BcrRobotVision.Services
{
    public class LogService
    {
        private readonly string _logDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        public void Write(string message)
        {
            if (!Directory.Exists(_logDir))
            {
                Directory.CreateDirectory(_logDir);
            }

            string filePath = Path.Combine(_logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
    }
}