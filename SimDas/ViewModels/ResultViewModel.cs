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
using SimDas.Models.Analysis;
using System.Collections.ObjectModel;

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
        private double _sliderTickFrequency = 1; // 기본값 설정
        private string _initialDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Datas");
        private readonly List<PlotInfo> _plots;
        private bool _showTimeIndicator = true;
        private SolverType _solverType;
        private DAEAnalysis _currentAnalysis;
        private string _systemAnalysis;
        private bool _showAnalysis;
        private double _conditionNumber;
        private double _stiffnessRatio;
        private ObservableCollection<string> _systemWarnings;
        private PopupPosition _analysisPopupPosition = new PopupPosition
        {
            X = 300, // 초기 X 좌표 (화면 중앙으로 조정)
            Y = 200  // 초기 Y 좌표
        };

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
            set
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
                }
            }
        }

        public double SliderTickFrequency
        {
            get => _sliderTickFrequency;
            set => SetProperty(ref _sliderTickFrequency, value);
        }

        public bool ShowTimeIndicator
        {
            get => _showTimeIndicator;
            set
            {
                if (SetProperty(ref _showTimeIndicator, value))
                {
                    UpdateTimeIndicators();
                }
            }
        }

        public SolverType SolverType
        {
            get => _solverType;
            set => SetProperty(ref _solverType, value);
        }

        public string SystemAnalysis
        {
            get => _systemAnalysis;
            set => SetProperty(ref _systemAnalysis, value);
        }

        public bool ShowAnalysis
        {
            get => _showAnalysis;
            set => SetProperty(ref _showAnalysis, value);
        }

        public double ConditionNumber
        {
            get => _conditionNumber;
            private set => SetProperty(ref _conditionNumber, value);
        }

        public double StiffnessRatio
        {
            get => _stiffnessRatio;
            private set => SetProperty(ref _stiffnessRatio, value);
        }

        public ObservableCollection<string> SystemWarnings
        {
            get => _systemWarnings;
            private set => SetProperty(ref _systemWarnings, value);
        }

        public bool HasWarnings => SystemWarnings?.Any() == true;

        public PopupPosition AnalysisPopupPosition
        {
            get => _analysisPopupPosition;
            set => SetProperty(ref _analysisPopupPosition, value);
        }

        public ICommand ExportToCsvCommand { get; }
        public ICommand CopyToClipboardCommand { get; }
        public ICommand SavePlotCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand CloseAnalysisCommand { get; }
        public ICommand ToggleAnalysisCommand { get; }


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
            CloseAnalysisCommand = new RelayCommand(() => ShowAnalysis = false);
            ToggleAnalysisCommand = new RelayCommand(() => ShowAnalysis = !ShowAnalysis);

            _analysisPopupPosition = new PopupPosition();

            DisplayFormat = 2;

            _plots = new List<PlotInfo>
            {
                new PlotInfo
                {
                    PlotControl = StatesPlotControl,
                    GetValues = index => _currentSolution.States[index]
                },
                new PlotInfo
                {
                    PlotControl = DerivativesPlotControl,
                    GetValues = index => _currentSolution.Derivatives[index]
                }
            };

            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.Closed += (s, e) => ShowAnalysis = false;
            }

            if (!Directory.Exists(_initialDirectory))
            {
                Directory.CreateDirectory(_initialDirectory);
                _loggingService.Info($"Directory created: {_initialDirectory}");
            }
        }

        public void UpdateAnalysis(DAEAnalysis analysis)
        {
            _currentAnalysis = analysis;

            var sb = new StringBuilder();
            sb.AppendLine($"DAE Index: {analysis.Index}");
            sb.AppendLine($"Stiff System: {(analysis.IsStiff ? "Yes" : "No")}");

            int algebraicCount = analysis.AlgebraicVariables.Count(x => x);
            sb.AppendLine($"Algebraic Variables: {algebraicCount}");

            if (analysis.Eigenvalues.Any())
            {
                sb.AppendLine("\nEigenvalues:");
                foreach (var eigenvalue in analysis.Eigenvalues.Take(5))
                {
                    if (Math.Abs(eigenvalue.Imaginary) < 1e-10)
                    {
                        sb.AppendLine($"  {eigenvalue.Real:E3}");
                    }
                    else
                    {
                        sb.AppendLine($"  {eigenvalue.Real:E3} + {eigenvalue.Imaginary:E3}i");
                    }
                }
            }

            StiffnessRatio = analysis.StiffnessRatio;

            // Condition Number가 무한대인 경우 처리
            if (double.IsInfinity(analysis.ConditionNumber))
            {
                ConditionNumber = -1; // UI에서 특별히 처리
            }
            else
            {
                ConditionNumber = analysis.ConditionNumber;
            }

            sb.AppendLine();
            sb.AppendLine("Numerical Properties:");
            sb.AppendLine($"Condition Number: {ConditionNumber:F2}");
            sb.AppendLine($"Stiffness Ratio: {StiffnessRatio:F2}");

            SystemAnalysis = sb.ToString();
            SystemWarnings = new ObservableCollection<string>(analysis.Warnings);

            ShowAnalysis = true;
            RaisePropertyChanged(nameof(HasWarnings));
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
                StatesPlotControl.Plot.Axes.NumericTicksBottom();

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
            Colors.Red,
            Colors.Green,
            Colors.Blue,
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
            if (_currentSolution == null) return;

            var timeArray = _currentSolution.TimePoints.ToArray();

            foreach (var plot in _plots)
            {
                plot.PlotControl.Plot.Clear();

                // Plot data
                for (int i = 0; i < plot.GetValues(0).Length; i++)
                {
                    var values = Enumerable.Range(0, _currentSolution.TimePoints.Count)
                        .Select(idx => plot.GetValues(idx)[i])
                        .ToArray();

                    var scatter = plot.PlotControl.Plot.Add.Scatter(
                        xs: timeArray,
                        ys: values,
                        color: colors[i % colors.Length]);
                }
                plot.PlotControl.Plot.Axes.AutoScale();
                plot.PlotControl.Refresh();
            }
        }

        private void UpdateTimeIndicators()
        {
            if (!HasResults) return;

            var (states, derivatives) = InterpolateValues(SelectedTime);
            var values = new[] { states, derivatives };

            for (int plotIndex = 0; plotIndex < _plots.Count; plotIndex++)
            {
                var plot = _plots[plotIndex];

                // 기존 마커 제거
                foreach (var marker in plot.Markers)
                {
                    plot.PlotControl.Plot.Remove(marker);
                }
                plot.Markers.Clear();
                plot.PlotControl.Plot.Remove(plot.TimeIndicator);

                if (_showTimeIndicator)
                {
                    // 새 마커 추가
                    var currentValues = values[plotIndex];
                    for (int i = 0; i < currentValues.Length; i++)
                    {
                        var value = currentValues[i];
                        var marker = plot.PlotControl.Plot.Add.Text($"{(plotIndex == 1 ? "d" : "")}{_variableNames[i]} : {value:+0.00;-0.00;0}", SelectedTime, value);

                        marker.LabelBackgroundColor = colors[i % colors.Length].WithAlpha(100);
                        marker.LabelFontColor = Colors.White;
                        marker.LabelFontSize = 12;
                        marker.LabelPadding = 3;
                        marker.LabelOffsetX = -3;
                        marker.LabelOffsetY = 3;
                        marker.LabelAlignment = Alignment.UpperRight;

                        plot.Markers.Add(marker);
                    }

                    plot.TimeIndicator = plot.PlotControl.Plot.Add.VerticalLine(SelectedTime, color: Colors.Gray);
                    plot.TimeIndicator.X = SelectedTime;
                }

                plot.PlotControl.Refresh();
            }
        }

        private (double[] states, double[] derivatives) InterpolateValues(double time)
        {
            var timePoints = _currentSolution.TimePoints;

            // 현재 시간이 위치할 수 있는 두 시점 찾기
            int upperIndex = timePoints.ToList().FindIndex(t => t >= time);

            // 경계 조건 처리
            if (upperIndex == -1) // time이 마지막 시점보다 크거나 같은 경우
            {
                return (_currentSolution.States.Last(), _currentSolution.Derivatives.Last());
            }
            if (upperIndex == 0) // time이 첫 시점보다 작은 경우
            {
                return (_currentSolution.States.First(), _currentSolution.Derivatives.First());
            }

            int lowerIndex = upperIndex - 1;

            double t0 = timePoints[lowerIndex];
            double t1 = timePoints[upperIndex];

            // 보간 계수 계산 (0 ~ 1 사이 값)
            double alpha = (time - t0) / (t1 - t0);

            // states 보간
            var states = new double[_currentSolution.States[0].Length];
            for (int i = 0; i < states.Length; i++)
            {
                double y0 = _currentSolution.States[lowerIndex][i];
                double y1 = _currentSolution.States[upperIndex][i];
                states[i] = LinearInterpolate(y0, y1, alpha);
            }

            // derivatives 보간
            var derivatives = new double[_currentSolution.Derivatives[0].Length];
            for (int i = 0; i < derivatives.Length; i++)
            {
                double y0 = _currentSolution.Derivatives[lowerIndex][i];
                double y1 = _currentSolution.Derivatives[upperIndex][i];
                derivatives[i] = LinearInterpolate(y0, y1, alpha);
            }

            return (states, derivatives);
        }

        private double LinearInterpolate(double y0, double y1, double alpha)
        {
            return y0 + (y1 - y0) * alpha;
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
                var defaultFileName = $"Results_{SolverType}_{timestamp}.csv";

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

                    _loggingService.Info($"Results exproted to {dialog.FileName}");
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

            foreach (var plot in _plots)
            {
                plot.TimeIndicator = null;
                plot.Markers.Clear();
                plot.PlotControl.Reset();
                plot.PlotControl.Refresh();
            }

            _loggingService.Info("Results cleared");
        }

        private class PlotInfo
        {
            public WpfPlot PlotControl { get; set; }
            public VerticalLine TimeIndicator { get; set; }
            public List<Text> Markers { get; set; } = new();
            public Func<int, double[]> GetValues { get; set; }
        }
    }
}