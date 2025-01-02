using SimDas.ViewModels.Base;
using SimDas.Models.Common;
using SimDas.Services;
using System.Windows.Input;
using ScottPlot;
using ScottPlot.WPF;
using ScottPlot.Plottables;
using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System;
using System.Threading.Tasks;

namespace SimDas.ViewModels
{
    public class ResultViewModel : ViewModelBase
    {
        private readonly ILoggingService _loggingService;
        private readonly IDialogService _dialogService;
        private readonly IPlottingService _plottingService;
        private Solution _currentSolution;
        private string _logContent;
        private double _selectedTime;
        private List<string> _variableNames;
        private bool _hasResults;
        private double _minTime;
        private double _maxTime;
        private int _displayFormat;
        private VerticalLine _statesTimeIndicator;
        private VerticalLine _derivativesTimeIndicator;
        private double _sliderTickFrequency = 1.0; // 기본값 설정
        private string _initialDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Datas");

        public WpfPlot StatesPlotControl { get; set; }
        public WpfPlot DerivativesPlotControl { get; set; }

        public string LogContent
        {
            get => _logContent;
            private set => SetProperty(ref _logContent, value);
        }

        public double SelectedTime
        {
            get => _selectedTime;
            set
            {
                if (SetProperty(ref _selectedTime, Math.Max(Math.Min(value, _maxTime), _minTime)))
                {
                    UpdateTimeIndicators();
                }
            }
        }

        public double MinTime
        {
            get => _minTime;
            private set => SetProperty(ref _minTime, value);
        }

        public double MaxTime
        {
            get => _maxTime;
            private set => SetProperty(ref _maxTime, value);
        }

        public bool HasResults
        {
            get => _hasResults;
            private set
            {
                if (SetProperty(ref _hasResults, value))
                {
                    _loggingService.Debug($"HasResults: {HasResults}");
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public int DisplayFormat
        {
            get => _displayFormat;
            set
            {
                if (SetProperty(ref _displayFormat, value))
                {
                    UpdateLogContent();
                    UpdateGraphMarkers(value);
                }
            }
        }

        public double SliderTickFrequency
        {
            get => _sliderTickFrequency;
            set => SetProperty(ref _sliderTickFrequency, value);
        }

        public ICommand ExportToCsvCommand { get; }
        public ICommand CopyToClipboardCommand { get; }
        public ICommand SavePlotCommand { get; }
        public ICommand ClearCommand { get; }

        public ResultViewModel(
            ILoggingService loggingService,
            IDialogService dialogService,
            IPlottingService plottingService)
        {
            _loggingService = loggingService;
            _dialogService = dialogService;
            _plottingService = plottingService;

            // Initialize plot controls
            StatesPlotControl = new WpfPlot();
            DerivativesPlotControl = new WpfPlot();

            // Initialize commands
            ExportToCsvCommand = new RelayCommand(ExecuteExportToCsv, () => HasResults);
            CopyToClipboardCommand = new RelayCommand(ExecuteCopyToClipboard, () => HasResults);
            SavePlotCommand = new RelayCommand(ExecuteSavePlot, () => HasResults);
            ClearCommand = new RelayCommand(ExecuteClear, () => HasResults);

            DisplayFormat = 2;
        }

        public void DisplayResults(Solution solution, List<string> variableNames)
        {
            try
            {
                _currentSolution = solution;
                _variableNames = variableNames;

                if (solution == null || solution.TimePoints == null || !solution.TimePoints.Any())
                {
                    _loggingService.Warning("Solution is empty or invalid.");
                    HasResults = false;
                    return;
                }

                HasResults = true;

                var timeArray = solution.TimePoints.ToArray();
                MinTime = timeArray.First();
                MaxTime = timeArray.Last();
                SelectedTime = MinTime;

                UpdatePlots();

                // 축 그리기
                StatesPlotControl.Plot.Add.VerticalLine(0, color: Colors.Black);
                DerivativesPlotControl.Plot.Add.VerticalLine(0, color: Colors.Black);
                StatesPlotControl.Plot.Add.HorizontalLine(0, color: Colors.Black);
                DerivativesPlotControl.Plot.Add.HorizontalLine(0, color: Colors.Black);

                UpdateLogContent();

                _loggingService.Info("Results displayed successfully");
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Error displaying results: {ex.Message}");
                _dialogService.ShowError($"Error displaying results: {ex.Message}");
            }
        }

        private readonly Color[] colors =
        {
            Colors.Blue,
            Colors.Red,
            Colors.Green,
            Colors.Orange,
            Colors.Purple,
            Colors.Brown,
            Colors.Pink,
            Colors.Gray,
            Colors.Cyan,
            Colors.Magenta
        };

        private void UpdatePlots()
        {
            StatesPlotControl.Plot.Clear();
            DerivativesPlotControl.Plot.Clear();
            var timeArray = _currentSolution.TimePoints.ToArray();

            // Plot states
            for (int i = 0; i < _currentSolution.States[0].Length; i++)
            {
                var values = _currentSolution.States.Select(state => state[i]).ToArray();
                var scatter = StatesPlotControl.Plot.Add.Scatter(
                    xs: timeArray,
                    ys: values,
                    color: colors[i % colors.Length]);
            }

            // Plot derivatives
            for (int i = 0; i < _currentSolution.Derivatives[0].Length; i++)
            {
                var values = _currentSolution.Derivatives.Select(deriv => deriv[i]).ToArray();
                var scatter = DerivativesPlotControl.Plot.Add.Scatter(
                    xs: timeArray,
                    ys: values,
                    color: colors[i % colors.Length]);
            }

            // Add time indicators
            _statesTimeIndicator = StatesPlotControl.Plot.Add.VerticalLine(SelectedTime, color : Colors.Gray);
            _derivativesTimeIndicator = DerivativesPlotControl.Plot.Add.VerticalLine(SelectedTime, color: Colors.Gray);

            StatesPlotControl.Refresh();
            DerivativesPlotControl.Refresh();
        }

        public async Task UpdatePlotsAsync(Solution solution)
        {
            await Task.Run(() =>
            {
                // TimePoints 배열 생성
                var timeArray = solution.TimePoints.ToArray();

                // StatesPlot 업데이트
                StatesPlotControl.Plot.Clear();
                for (int i = 0; i < solution.States[0].Length; i++)
                {
                    var values = solution.States.Select(state => state[i]).ToArray();
                    StatesPlotControl.Plot.Add.Scatter(timeArray, values);
                }
                StatesPlotControl.Plot.Axes.AutoScale();
                StatesPlotControl.Refresh();

                // DerivativesPlot 업데이트
                DerivativesPlotControl.Plot.Clear();
                for (int i = 0; i < solution.Derivatives[0].Length; i++)
                {
                    var values = solution.Derivatives.Select(derivative => derivative[i]).ToArray();
                    DerivativesPlotControl.Plot.Add.Scatter(timeArray, values);
                }
                DerivativesPlotControl.Plot.Axes.AutoScale();
                DerivativesPlotControl.Refresh();
            });
        }

        private void UpdateTimeIndicators()
        {
            if (!HasResults) return;

            // 타임 인디케이터 초기화
            if (_statesTimeIndicator == null)
            {
                _statesTimeIndicator = StatesPlotControl.Plot.Add.VerticalLine(SelectedTime, color:Colors.Gray);
            }
            else
            {
                _statesTimeIndicator.X = SelectedTime;
                _statesTimeIndicator.IsVisible = true;
            }

            if (_derivativesTimeIndicator == null)
            {
                _derivativesTimeIndicator = DerivativesPlotControl.Plot.Add.VerticalLine(SelectedTime, color:Colors.Gray);
            }
            else
            {
                _derivativesTimeIndicator.X = SelectedTime;
                _derivativesTimeIndicator.IsVisible = true;
            }

            // 플롯 업데이트
            StatesPlotControl.Refresh();
            DerivativesPlotControl.Refresh();
        }

        private void UpdateLogContent()
        {
            if (!HasResults) return;

            var format = DisplayFormat switch
            {
                0 => "G6",  // 일반
                1 => "E3",  // 지수
                2 => "F6", // 고정소수점
                _ => "G"
            };

            var log = new StringBuilder();

            // 헤더 작성
            log.AppendLine("Time\t\t" + string.Join("\t\t",
                _variableNames ?? Enumerable.Range(0, _currentSolution.States[0].Length)
                                         .Select(i => $"State{i}")));

            // 데이터 작성
            for (int i = 0; i < _currentSolution.TimePoints.Count; i++)
            {
                log.Append($"{_currentSolution.TimePoints[i].ToString(format)}\t");
                log.AppendLine(string.Join("\t", _currentSolution.States[i]
                    .Select(v => v.ToString(format))));
            }

            LogContent = log.ToString();
        }

        private void ExecuteExportToCsv()
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var defaultFileName = $"Results_{timestamp}.csv";

                var dialog = new SaveFileDialog
                {
                    FileName = defaultFileName,
                    Filter = "CSV files (*.csv)|*.csv",
                    DefaultExt = "csv",
                    AddExtension = true,
                    InitialDirectory = _initialDirectory
                };

                if (dialog.ShowDialog() == true)
                {
                    using var writer = new StreamWriter(dialog.FileName);

                    // Write header
                    writer.Write("Time");
                    foreach (var name in _variableNames ??
                        Enumerable.Range(0, _currentSolution.States[0].Length)
                                .Select(i => $"State{i}"))
                    {
                        writer.Write($",{name},d{name}/dt");
                    }
                    writer.WriteLine();

                    // Write data
                    for (int i = 0; i < _currentSolution.TimePoints.Count; i++)
                    {
                        writer.Write(_currentSolution.TimePoints[i].ToString("G"));
                        for (int j = 0; j < _currentSolution.States[0].Length; j++)
                        {
                            writer.Write($",{_currentSolution.States[i][j]:G}");
                            writer.Write($",{_currentSolution.Derivatives[i][j]:G}");
                        }
                        writer.WriteLine();
                    }

                }
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Failed to export results: {ex.Message}");
                _dialogService.ShowError($"Failed to export results: {ex.Message}");
            }
        }

        private void ExecuteCopyToClipboard()
        {
            try
            {
                Clipboard.SetText(LogContent);
                _loggingService.Info("Results copied to clipboard");
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Failed to copy results: {ex.Message}");
                _dialogService.ShowError($"Failed to copy results: {ex.Message}");
            }
        }

        private void ExecuteSavePlot()
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var defaultFileName = $"Graphs_{timestamp}.png";

                var dialog = new SaveFileDialog
                {
                    FileName = defaultFileName,
                    Filter = "PNG images (*.png)|*.png",
                    DefaultExt = "png",
                    AddExtension = true,
                    InitialDirectory = _initialDirectory
                };

                if (dialog.ShowDialog() == true)
                {
                    var basePath = Path.GetDirectoryName(dialog.FileName);
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(dialog.FileName);

                    var statesPath = Path.Combine(basePath, $"{fileNameWithoutExt}_states.png");
                    var derivativesPath = Path.Combine(basePath, $"{fileNameWithoutExt}_derivatives.png");

                    StatesPlotControl.Plot.SavePng(statesPath, 800, 600);
                    DerivativesPlotControl.Plot.SavePng(derivativesPath, 800, 600);

                    _loggingService.Info($"Plots saved to {basePath}");
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Failed to save plots: {ex.Message}");
                _dialogService.ShowError($"Failed to save plots: {ex.Message}");
            }
        }

        private void ExecuteClear()
        {
            _currentSolution = null;
            _variableNames = null;
            HasResults = false;
            LogContent = string.Empty;
            MinTime = 0;
            MaxTime = 0;
            SelectedTime = 0;

            // 타임 인디케이터 제거
            _statesTimeIndicator = null;
            _derivativesTimeIndicator = null;

            StatesPlotControl.Plot.Clear();
            DerivativesPlotControl.Plot.Clear();

            StatesPlotControl.Refresh();
            DerivativesPlotControl.Refresh();

            _loggingService.Info("Results cleared");
        }

        public void UpdateGraphMarkers(double time)
        {
            /*// 선택된 시간에 해당하는 데이터 값 계산
            var index = (int)(time / TimeStep); // TimeStep은 데이터 간 간격
            if (index >= DataX.Length) return; // 데이터 범위 초과 시 무시

            // 그래프 데이터
            SelectedDataX = DataX[index];
            SelectedDataY1 = DataY1[index]; // 그래프 1 데이터
            SelectedDataY2 = DataY2[index]; // 그래프 2 데이터

            // 그래프 업데이트
            UpdateGraph();*/
        }
    }
}