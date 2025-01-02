using SimDas.ViewModels.Base;
using SimDas.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace SimDas.ViewModels
{
    public class LogViewModel : ViewModelBase
    {
        private readonly ILoggingService _loggingService; 
        private LogLevel _selectedLogLevel;
        private string _initialDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Datas");

        public LogLevel SelectedLogLevel
        {
            get => _selectedLogLevel;
            set
            {
                if (SetProperty(ref _selectedLogLevel, value))
                {
                    _loggingService.CurrentLogLevel = value;
                }
            }
        }

        public IEnumerable<LogLevel> LogLevels { get; } = Enum.GetValues(typeof(LogLevel)).Cast<LogLevel>();

        public ObservableCollection<LogEntry> LogEntries { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand SaveLogsCommand { get; }
        public ICommand TestLogsCommand { get; }

        public LogViewModel(ILoggingService loggingService)
        {
            _loggingService = loggingService;
            LogEntries = new ObservableCollection<LogEntry>();

            _loggingService.OnLogAdded += (sender, log) =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    LogEntries.Add(log);
                });

                (ClearLogsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            };

            ClearLogsCommand = new RelayCommand(ClearLogs, () => LogEntries.Count > 1);
            SaveLogsCommand = new RelayCommand(SaveLogs);
            TestLogsCommand = new RelayCommand(TestMessage);

            // 디폴트 로그 수준 설정
            SelectedLogLevel = LogLevel.Info;
            _loggingService.CurrentLogLevel = SelectedLogLevel;
        }

        private void ClearLogs()
        {
            LogEntries.Clear();
            _loggingService.Info("Log view cleared.");
        }

        private void SaveLogs()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var defaultFileName = $"Logs_{timestamp}.txt";

            var dialog = new SaveFileDialog
            {
                FileName = defaultFileName,
                Filter = "Text files (*.txt)|*.txt",
                DefaultExt = "txt",
                AddExtension = true,
                InitialDirectory = _initialDirectory
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                using (var writer = new StreamWriter(dialog.FileName))
                {
                    foreach (var log in LogEntries)
                    {
                        writer.WriteLine(log.ToString());
                    }
                }

                _loggingService.Info($"Logs saved to {dialog.FileName}");
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Failed to save logs: {ex.Message}");
            }
        }

        private void TestMessage()
        {
            _loggingService.Log("This is a debug message", LogLevel.Debug);
            _loggingService.Log("This is an info message", LogLevel.Info);
            _loggingService.Log("This is a warning message", LogLevel.Warning);
            _loggingService.Log("This is an error message", LogLevel.Error);
        }
    }
}
