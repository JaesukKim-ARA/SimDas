using SimDas.ViewModels.Base;
using SimDas.Services;
using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Windows.Input;
using System.Threading;
using System;
using System.Threading.Tasks;
using SimDas.Models.Analysis;
using System.Linq;
using System.Windows;

namespace SimDas.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;
        private readonly ILoggingService _loggingService;
        private ISolver _currentSolver;
        private readonly DAEAnalyzer _daeAnalyzer;
        private bool _isSolving;
        private bool _isPaused;
        private double _progress;
        private string _statusMessage;
        private CancellationTokenSource _cancellationTokenSource;

        public InputViewModel InputViewModel { get; }
        public SolverSettingsViewModel SolverSettingsViewModel { get; }
        public ResultViewModel ResultViewModel { get; }
        public LogViewModel LogViewModel { get; }

        public ICommand SolveCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand SaveResultsCommand { get; }
        public ICommand InputClearCommand { get; }

        public bool IsSolving
        {
            get => _isSolving;
            private set => SetProperty(ref _isSolving, value);
        }

        public bool IsPaused
        {
            get => _isPaused;
            private set => SetProperty(ref _isPaused, value);
        }

        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public MainViewModel(
            IDialogService dialogService,
            ILoggingService loggingService,
            InputViewModel inputViewModel,
            SolverSettingsViewModel solverSettingsViewModel,
            ResultViewModel resultViewModel,
            DAEAnalyzer daeAnalyzer)
        {
            _dialogService = dialogService;
            _loggingService = loggingService;

            InputViewModel = inputViewModel;
            SolverSettingsViewModel = solverSettingsViewModel;
            ResultViewModel = resultViewModel;
            _daeAnalyzer = daeAnalyzer;

            SolveCommand = new RelayCommand(ExecuteSolve, CanExecuteSolve);
            PauseCommand = new RelayCommand(ExecutePause, () => IsSolving);
            StopCommand = new RelayCommand(ExecuteStop, () => IsSolving);
            SaveResultsCommand = new RelayCommand(ExecuteSaveResults, CanExecuteSaveResults);
            InputClearCommand = new RelayCommand(ExecuteInputClear, CanExecuteInputClear);

            // SolverSettingsViewModel의 SolverType을 InputViewModel과 동기화
            SolverSettingsViewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(SolverSettingsViewModel.SelectedSolverType))
                {
                    InputViewModel.SolverType = SolverSettingsViewModel.SelectedSolverType;
                }
            };
            LogViewModel = new LogViewModel(loggingService);
        }

        private bool CanExecuteSolve() => !IsSolving && InputViewModel.IsValid;

        private async void ExecuteSolve()
        {
            try
            {
                IsSolving = true;
                Progress = 0;
                StatusMessage = "Analyzing system...";

                (var daeSystem, var dimension) = InputViewModel.ParseEquations();

                using var cts = new CancellationTokenSource();
                var progress = new Progress<AnalysisProgress>(p =>
                {
                    StatusMessage = p.Message;
                    Progress = p.Percentage;
                });

                // 비동기 분석 실행
                var analysis = await _daeAnalyzer.AnalyzeSystemAsync(
                    (DAESystem)daeSystem,
                    dimension,
                    InputViewModel.GetInitialState(),
                    InputViewModel.StartTime,
                    cts.Token);

                // 분석 결과 처리
                ResultViewModel.UpdateAnalysis(analysis);

                if (analysis.Warnings.Any())
                {
                    _dialogService.ShowWarning(
                        string.Join("\n", analysis.Warnings),
                        "DAE System Analysis");
                }

                // 부적절한 솔버 선택 경고
                if (analysis.IsStiff &&
                    (SolverSettingsViewModel.SelectedSolverType == SolverType.ExplicitEuler ||
                     SolverSettingsViewModel.SelectedSolverType == SolverType.RungeKutta4))
                {
                    if (!_dialogService.ShowConfirmation(
                        "Stiff system detected. Explicit methods may be unstable.\nContinue anyway?",
                        "Solver Warning"))
                    {
                        IsSolving = false;
                        return;
                    }
                }

                StatusMessage = "Initializing solver...";

                _cancellationTokenSource = new CancellationTokenSource();
                _currentSolver = SolverSettingsViewModel.CreateSolver();
                _currentSolver.OnProgressChanged += Solver_OnProgressChanged;

                var parameters = InputViewModel.GetParameters();
                var initialState = InputViewModel.GetInitialState();

                _currentSolver.SetDAESystem(daeSystem, dimension);

                _currentSolver.InitialState = initialState;
                _currentSolver.Initialize(parameters);
                _currentSolver.StartTime = InputViewModel.StartTime;
                _currentSolver.EndTime = InputViewModel.EndTime;
                _currentSolver.Intervals = SolverSettingsViewModel.Intervals;

                _loggingService.Info($"Starting solution with {_currentSolver.Name}");

                var solution = await Task.Run(() => _currentSolver.SolveAsync(_cancellationTokenSource.Token));

                if (solution != null)
                {
                    ResultViewModel.DisplayResults(solution, InputViewModel.GetVariableNames());
                    _loggingService.Info("Solution completed successfully");
                }
                else
                {
                    _loggingService.Warning("Solution is null. No results to display");
                }
            }
            catch (OperationCanceledException)
            {
                _loggingService.Warning("Operation was cancelled by user");
                StatusMessage = "Operation cancelled";
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Error during solution: {ex.Message}");
                _dialogService.ShowError(ex.Message);
            }
            finally
            {
                IsSolving = false;
                Progress = 0;
                StatusMessage = "Ready";
                _currentSolver?.Cleanup();
                _currentSolver = null;
                _cancellationTokenSource?.Dispose();
            }
        }

        private void ExecutePause()
        {
            try
            {
                if (_currentSolver == null)
                    return;

                if (_currentSolver.IsPaused)
                {
                    _currentSolver.Resume();
                    IsPaused = false;
                    StatusMessage = "Resuming...";
                    _loggingService.Info("Solution resuming");
                }
                else
                {
                    _currentSolver.Pause();
                    IsPaused = true;
                    StatusMessage = "Paused";
                    _loggingService.Info("Solution paused");
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Error in pause/resume: {ex.Message}");
                _dialogService.ShowError(ex.Message);
            }
        }

        private void ExecuteStop()
        {
            if (_currentSolver != null)
            {
                // 일시정지 상태인 경우 먼저 Resume 처리
                if (_currentSolver.IsPaused)
                {
                    _currentSolver.Resume();
                    IsPaused = false;
                }

                // 취소 처리
                _cancellationTokenSource?.Cancel();
                StatusMessage = "Stopped";
                _loggingService.Info("Solution stopped by user");
            }
        }

        private bool CanExecuteSaveResults() => ResultViewModel.HasResults && !IsSolving;

        private void ExecuteSaveResults()
        {
            var filePath = _dialogService.ShowSaveFileDialog(".csv", "CSV files (*.csv)|*.csv");
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    ResultViewModel.ExportToCsvCommand.Execute(filePath);
                    _loggingService.Info($"Results saved to {filePath}");
                }
                catch (Exception ex)
                {
                    _loggingService.Error($"Failed to save results: {ex.Message}");
                    _dialogService.ShowError($"Failed to save results: {ex.Message}");
                }
            }
        }

        private bool CanExecuteInputClear() => !IsSolving;

        private void ExecuteInputClear()
        {
            if (_dialogService.ShowConfirmation("Are you sure you want to clear all data?"))
            {
                InputViewModel.Clear();
                ResultViewModel.ClearCommand.Execute(null);
                _loggingService.Clear();
                LogViewModel.ClearLogsCommand.Execute(null);
                _loggingService.Info("All data cleared");
            }
        }

        private void Solver_OnProgressChanged(object sender, ProgressEventArgs e)
        {
            Progress = e.ProgressPercentage;

            if (Progress >= 100)
            {
                Progress = 100.0; // 명시적으로 100%로 고정
            }

            StatusMessage = e.Status;
        }
    }
}