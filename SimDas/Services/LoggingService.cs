using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace SimDas.Services
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; }
        public string Message { get; }
        public LogLevel Level { get; }
        public Brush TextColor { get; }

        public LogEntry(string message, LogLevel level, Brush textColor)
        {
            Timestamp = DateTime.Now;
            Message = message;
            Level = level;
            TextColor = textColor;
        }

        public override string ToString() => $"[{Timestamp:HH:mm:ss.fff}] [{Level}] {Message}";
    }

    public interface ILoggingService
    {
        event EventHandler<LogEntry> OnLogAdded;
        LogLevel CurrentLogLevel { get; set; }

        void Log(string message, LogLevel level = LogLevel.Info);
        void Debug(string message);
        void Info(string message);
        void Warning(string message);
        void Error(string message);
        void SaveLogs(string filePath);
        void Clear();
    }

    public class LoggingService : ILoggingService
    {
        private readonly ConcurrentQueue<LogEntry> logEntries = new();
        public event EventHandler<LogEntry> OnLogAdded;
        public LogLevel CurrentLogLevel { get; set; } = LogLevel.Info;

        private readonly Dictionary<LogLevel, Brush> logColors = new()
        {
            { LogLevel.Debug, Brushes.Gray },
            { LogLevel.Info, Brushes.Black },
            { LogLevel.Warning, Brushes.Orange },
            { LogLevel.Error, Brushes.Red }
        };

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (level < CurrentLogLevel)
                return;

            var entry = new LogEntry(message, level, logColors[level]);
            logEntries.Enqueue(entry);
            OnLogAdded?.Invoke(this, entry);
        }

        public void Debug(string message) => Log(message, LogLevel.Debug);
        public void Info(string message) => Log(message, LogLevel.Info);
        public void Warning(string message) => Log(message, LogLevel.Warning);
        public void Error(string message) => Log(message, LogLevel.Error);

        public void SaveLogs(string filePath)
        {
            try
            {
                using var writer = new StreamWriter(filePath);
                foreach (var entry in logEntries)
                {
                    writer.WriteLine(entry.ToString());
                }
            }
            catch (Exception ex)
            {
                Error($"Failed to save logs: {ex.Message}");
                throw;
            }
        }

        public void Clear()
        {
            while (logEntries.TryDequeue(out _)) { }
            Info("Log cleared");
        }
    }
}