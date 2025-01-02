using SimDas.ViewModels.Base;
using SimDas.Services;
using SimDas.Parser;
using System.Collections.Generic;
using System.Linq;
using System;
using SimDas.Models.Common;

namespace SimDas.ViewModels
{
    public class InputViewModel : ViewModelBase
    {
        private readonly ILoggingService _loggingService;
        private readonly EquationParser _equationParser;
        private string _equationInput = string.Empty;
        private string _parameterInput = string.Empty;
        private string _initialValueInput = string.Empty;
        private double _startTime;
        private double _endTime = 10.0;
        private bool _isValid; 
        private SolverType _solverType;

        public SolverType SolverType
        {
            get => _solverType;
            set => SetProperty(ref _solverType, value);
        }

        public string EquationInput
        {
            get => _equationInput;
            set => SetProperty(ref _equationInput, value, ValidateInputs);
        }

        public string ParameterInput
        {
            get => _parameterInput;
            set => SetProperty(ref _parameterInput, value, ValidateInputs);
        }

        public string InitialValueInput
        {
            get => _initialValueInput;
            set => SetProperty(ref _initialValueInput, value, ValidateInputs);
        }

        public double StartTime
        {
            get => _startTime;
            set => SetProperty(ref _startTime, value, ValidateInputs);
        }

        public double EndTime
        {
            get => _endTime;
            set => SetProperty(ref _endTime, value, ValidateInputs);
        }

        public bool IsValid
        {
            get => _isValid;
            private set => SetProperty(ref _isValid, value);
        }

        public InputViewModel(ILoggingService loggingService)
        {
            SolverType = SolverType.DASSL;
            _loggingService = loggingService;
            _equationParser = new EquationParser(_loggingService);
            SetExampleEquations();
        }

        private void SetExampleEquations()
        {
            // Mass-Spring-Damper example
            EquationInput = "der(x) = v\r\nder(v) = (-k*x - c*v)/m";
            ParameterInput = "k=2; c=0.5; m=1";
            InitialValueInput = "x=1; v=0";
            StartTime = 0;
            EndTime = 10;
        }

        private void ValidateInputs()
        {
            try
            {
                IsValid = false;

                // Validate time range
                if (EndTime <= StartTime)
                {
                    _loggingService.Warning("End time must be greater than start time");
                    return;
                }

                // Parse equations
                var equations = EquationInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(eq => eq.Trim())
                    .Where(eq => !string.IsNullOrEmpty(eq))
                    .ToList();

                if (!equations.Any())
                {
                    _loggingService.Warning("No equations provided");
                    return;
                }

                // Parse parameters
                var parameters = ParseParameters();
                if (parameters == null)
                    return;

                // Parse initial conditions
                var initialConditions = InitialValueInput
                    .Split(';')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                if (!initialConditions.Any())
                {
                    _loggingService.Warning("No initial conditions provided");
                    return;
                }

                // Set valid variables from initial conditions
                var validVariables = initialConditions
                    .Select(cond => cond.Split('=')[0].Trim())
                    .ToList();

                _equationParser.GetExpressionParser().SetValidVariables(validVariables);
                _equationParser.SetParameters(parameters);

                // Try parsing the system

                int dimension;

                if (SolverType == SolverType.DASSL)
                {
                    (_, dimension) = _equationParser.ParseDAE(equations);
                }
                else
                {
                    (_, dimension) = _equationParser.Parse(equations);
                }

                if (dimension > 0)
                {
                    IsValid = true;
                    _loggingService.Debug("Input validation successful");
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Validation error: {ex.Message}");
                IsValid = false;
            }
        }


        private Dictionary<string, double> ParseParameters()
        {
            try
            {
                var parameters = new Dictionary<string, double>();
                var paramPairs = ParameterInput.Split(';')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s));

                foreach (var pair in paramPairs)
                {
                    var parts = pair.Split('=');
                    if (parts.Length != 2)
                    {
                        _loggingService.Error($"Invalid parameter format: {pair}");
                        return null;
                    }

                    string name = parts[0].Trim();
                    if (!double.TryParse(parts[1].Trim(), out double value))
                    {
                        _loggingService.Error($"Invalid number format in parameter: {pair}");
                        return null;
                    }

                    parameters[name] = value;
                }

                return parameters;
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Parameter parsing error: {ex.Message}");
                return null;
            }
        }

        public double[] GetInitialState()
        {
            var initialConditions = InitialValueInput
                .Split(';')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            return _equationParser.ParseInitialConditions(initialConditions);
        }

        public Dictionary<string, double> GetParameters() => ParseParameters();

        public List<string> GetVariableNames() => _equationParser.GetVariableNames();

        public List<string> GetEquations()
        {
            var equations = EquationInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(eq => eq.Trim())
                    .Where(eq => !string.IsNullOrEmpty(eq))
                    .ToList();

            return equations;
        }

        public (object equationSystem, int dimension) ParseEquations()
        {
            var equations = EquationInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(eq => eq.Trim())
                    .Where(eq => !string.IsNullOrEmpty(eq))
                    .ToList();

            return SolverType == SolverType.DASSL
            ? _equationParser.ParseDAE(equations)
            : _equationParser.Parse(equations);
        }

        public void Clear()
        {
            EquationInput = string.Empty;
            ParameterInput = string.Empty;
            InitialValueInput = string.Empty;
            StartTime = 0;
            EndTime = 10;
            IsValid = false;
            _equationParser.Reset();
        }
    }
}