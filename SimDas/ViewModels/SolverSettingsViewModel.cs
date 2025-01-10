using SimDas.ViewModels.Base;
using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using SimDas.Models.Solver.Fixed;
using SimDas.Models.Solver.Variable;
using System;
using SimDas.Services;
using System.Linq;

namespace SimDas.ViewModels
{
    public class SolverSettingsViewModel : ViewModelBase
    {
        private readonly ILoggingService _loggingService;
        private readonly IDialogService _dialogService;
        private SolverType _selectedSolverType;
        private int _intervals = 1000;

        public SolverType SelectedSolverType
        {
            get => _selectedSolverType;
            set
            {
                if (SetProperty(ref _selectedSolverType, value))
                {
                    _loggingService.Info($"Solver Changed : {_selectedSolverType}");
                }
            }
        }

        public int Intervals
        {
            get => _intervals;
            set => SetProperty(ref _intervals, value);
        }

        public Array AvailableSolvers => Enum.GetValues(typeof(SolverType));

        public ISolver CreateSolver()
        {
            return SelectedSolverType switch
            {
                SolverType.ExplicitEuler => new ExplicitEulerSolver(),
                SolverType.ImplicitEuler => new ImplicitEulerSolver(),
                SolverType.RungeKutta4 => new RungeKutta4Solver(),
                SolverType.DASSL => new DasslSolver(),
                _ => throw new ArgumentException($"Unknown solver type: {SelectedSolverType}")
            };
        }

        public SolverSettingsViewModel(
            ILoggingService loggingService,
            IDialogService dialogService)
        {
            // 기본값으로 DASSL 설정
            SelectedSolverType = SolverType.ExplicitEuler;
            _loggingService = loggingService;
            _dialogService = dialogService;
        }

        // 고급 설정을 위한 속성들
        private double _relativeTolerance = 1e-6;
        private double _absoluteTolerance = 1e-8;
        private int _maxOrder = 5;
        private int _maxNewtonIterations = 10;
        private double _initialStepSize = 1e-4;

        public double RelativeTolerance
        {
            get => _relativeTolerance;
            set => SetProperty(ref _relativeTolerance, value);
        }

        public double AbsoluteTolerance
        {
            get => _absoluteTolerance;
            set => SetProperty(ref _absoluteTolerance, value);
        }

        public int MaxOrder
        {
            get => _maxOrder;
            set => SetProperty(ref _maxOrder, Math.Max(1, Math.Min(5, value)));
        }

        public int MaxNewtonIterations
        {
            get => _maxNewtonIterations;
            set => SetProperty(ref _maxNewtonIterations, Math.Max(1, value));
        }

        public double InitialStepSize
        {
            get => _initialStepSize;
            set => SetProperty(ref _initialStepSize, Math.Max(1e-10, value));
        }

        public void ApplyAdvancedSettings(ISolver solver)
        {
            if (solver is DasslSolver dasslSolver)
            {
                dasslSolver.SetAdvancedSettings(
                    relativeTolerance: RelativeTolerance,
                    absoluteTolerance: AbsoluteTolerance,
                    maxOrder: MaxOrder,
                    maxNewtonIterations: MaxNewtonIterations,
                    initialStepSize: InitialStepSize
                );
            }
        }
    }
}