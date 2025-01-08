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

        public string EquationInput
        {
            get => _equationInput;
            set
            {

                if (SetProperty(ref _equationInput, value, ValidateInputs))
                {
                    // 방정식이 변경되면 변수 목록 초기화
                    UpdateValidVariables();
                    ValidateInputs();
                }
            }
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
            SetExampleEquations();
        }

        private void UpdateValidVariables()
        {
            try
            {
                // 새로운 방정식 파싱
                var equations = EquationInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(eq => eq.Trim())
                                             .Where(eq => !string.IsNullOrEmpty(eq))
                                             .ToList();

                // 방정식에서 유효 변수 추출
                var variables = new HashSet<string>();
                foreach (var equation in equations)
                {
                    var (leftSide, _) = SplitEquation(equation);
                    var varName = ParseVariableName(leftSide);
                    if (!string.IsNullOrEmpty(varName))
                    {
                        variables.Add(varName);
                    }
                }

                // 현재 초기값 파싱
                var currentInitialValues = new Dictionary<string, double>();
                var initialValuePairs = InitialValueInput.Split(';')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s));

                foreach (var pair in initialValuePairs)
                {
                    var parts = pair.Split('=');
                    if (parts.Length == 2 && double.TryParse(parts[1].Trim(), out double value))
                    {
                        currentInitialValues[parts[0].Trim()] = value;
                    }
                }

                // 새로운 초기값 문자열 생성
                var newInitialValues = new List<string>();
                foreach (var variable in variables)
                {
                    if (currentInitialValues.TryGetValue(variable, out double value))
                    {
                        newInitialValues.Add($"{variable}={value}");
                    }
                    else
                    {
                        newInitialValues.Add($"{variable}=0"); // 새로운 변수는 기본값 0으로 설정
                    }
                }

                // 초기값 업데이트
                InitialValueInput = string.Join("; ", newInitialValues);

                // ExpressionParser 업데이트
                _equationParser.GetExpressionParser().SetValidVariables(variables.ToList());

                _loggingService.Debug($"Valid variables updated: {string.Join(", ", variables)}");
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Failed to update valid variables: {ex.Message}");
            }
        }

        private (string leftSide, string rightSide) SplitEquation(string equation)
        {
            var parts = equation.Split('=');
            if (parts.Length != 2)
                throw new Exception($"Invalid equation format: {equation}");
            return (parts[0].Trim(), parts[1].Trim());
        }

        private string ParseVariableName(string leftSide)
        {
            // der(x) 형태 처리
            if (leftSide.StartsWith("der(") && leftSide.EndsWith(")"))
            {
                return leftSide.Substring(4, leftSide.Length - 5);
            }
            return leftSide;
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