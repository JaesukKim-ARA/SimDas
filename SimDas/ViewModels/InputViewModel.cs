using SimDas.ViewModels.Base;
using SimDas.Services;
using SimDas.Parser;
using System.Collections.Generic;
using System.Linq;
using System;
using SimDas.Models.Common;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows;
using System.Collections.ObjectModel;

namespace SimDas.ViewModels
{
    public class InputViewModel : ViewModelBase
    {
        private readonly ILoggingService _loggingService;
        private readonly EquationParser _equationParser;
        private string _equationInput = string.Empty;
        private string _parameterInput = string.Empty;
        private string _initialValueInput = string.Empty;
        private SolverType _solverType;
        private double _startTime;
        private double _endTime = 10.0;
        private bool _isValid;

        public string EquationInput
        {
            get => _equationInput;
            set => SetProperty(ref _equationInput, value);
        }

        public SolverType SolverType
        {
            get => _solverType;
            set => SetProperty(ref _solverType, value);
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
            _loggingService = loggingService;
            _equationParser = new EquationParser(_loggingService);
        }

        private void ValidateInputs()
        {
            try
            {
                IsValid = false;

                if (EndTime <= StartTime)
                {
                    _loggingService.Warning("End time must be greater than start time");
                    return;
                }

                var parameters = ParseParameters();
                if (parameters == null)
                    return;

                _equationParser.SetParameters(parameters);

                var equations = EquationInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(eq => eq.Trim())
                    .Where(eq => !string.IsNullOrEmpty(eq))
                    .ToList();

                if (!equations.Any())
                {
                    _loggingService.Warning("No equations provided");
                    return;
                }

                // 시스템 파싱 시도
                try
                {
                    int dimension;
                    (_, dimension) = _equationParser.ParseDAE(equations);

                    if (dimension > 0)
                    {
                        IsValid = true;
                        _loggingService.Debug("Input validation successful");
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.Error($"Equation parsing error: {ex.Message}");
                    return;
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
            try
            {
                // 방정식을 기반으로 변수 동기화
                SyncExpressionParserVariables();

                // 초기 조건 파싱
                var initialConditions = InitialValueInput
                    .Split(';')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                var parsedInitialConditions = _equationParser.ParseInitialConditions(initialConditions);

                // 방정식에서 사용된 모든 변수가 초기 조건에 포함되었는지 확인
                var validVariables = _equationParser.GetExpressionParser().GetVariables();
                foreach (var variable in validVariables.Keys)
                {
                    if (!initialConditions.Any(cond => cond.StartsWith($"{variable}=")))
                    {
                        throw new Exception($"Missing initial condition for variable: {variable}");
                    }
                }

                return parsedInitialConditions;
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Error parsing initial state: {ex.Message}");
                throw;
            }
        }

        private void SyncExpressionParserVariables()
        {
            try
            {
                // 방정식에서 변수 추출
                var equations = EquationInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(eq => eq.Trim())
                                             .Where(eq => !string.IsNullOrEmpty(eq))
                                             .ToList();

                var variables = new HashSet<string>();
                foreach (var equation in equations)
                {
                    var tokens = _equationParser.GetExpressionParser().Tokenize(equation);
                    foreach (var token in tokens)
                    {
                        if (token.Type == TokenType.Variable)
                        {
                            variables.Add(token.Value);
                        }
                    }
                }

                // ExpressionParser의 변수 동기화
                _equationParser.GetExpressionParser().SetValidVariables(variables.ToList());

                _loggingService.Debug($"ExpressionParser variables synchronized: {string.Join(", ", variables)}");
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Error syncing variables: {ex.Message}");
                throw;
            }
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

        public (DAESystem daeSystem, int dimension) ParseEquations()
        {
            var equations = EquationInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(eq => eq.Trim())
                    .Where(eq => !string.IsNullOrEmpty(eq))
                    .ToList();

            return _equationParser.ParseDAE(equations);
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