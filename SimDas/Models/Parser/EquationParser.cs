using System.Collections.Generic;
using System.Linq;
using System;
using System.Text.RegularExpressions;
using SimDas.Models.Common;
using SimDas.Services;

namespace SimDas.Parser
{
    public class EquationParser
    {
        private readonly ExpressionParser _expressionParser;
        private readonly HashSet<string> _parameters;
        private readonly ILoggingService _loggingService;
        private bool _isInMultilineComment = false;

        public EquationParser(ILoggingService loggingService)
        {
            _loggingService = loggingService;
            _expressionParser = new ExpressionParser();
            _parameters = new HashSet<string>();
        }

        public ExpressionParser GetExpressionParser() => _expressionParser;

        public void SetParameters(Dictionary<string, double> parameters)
        {
            _loggingService.Info("Setting parameters...");
            _parameters.Clear();
            foreach (var param in parameters)
            {
                _parameters.Add(param.Key);
                _loggingService.Debug($"Parameter: {param.Key} = {param.Value}");
            }
            _expressionParser.SetParameters(parameters);
            _loggingService.Info("Parameters set successfully.");
        }

        public void Reset()
        {
            _expressionParser.Reset();
            _parameters.Clear();
        }

        public List<string> GetVariableNames() =>
            _expressionParser.GetVariables()
                .OrderBy(v => v.Value)
                .Select(v => v.Key)
                .ToList();

        public (DAESystem daeSystem, int dimension) ParseDAE(List<string> equations)
        {
            _loggingService.Info("Parsing equations...");
            _isInMultilineComment = false;  // 파싱 시작 시 상태 초기화

            var parsedEquations = new List<DAEEquation>();
            var variables = new Dictionary<string, int>();

            // 먼저 주석을 제거하고 유효한 방정식만 필터링
            var validEquations = equations
                .Select(eq => RemoveComments(eq))
                .Where(eq => !string.IsNullOrWhiteSpace(eq))
                .ToList();

            foreach (var equation in validEquations)
            {
                var (leftSide, rightSide) = SplitEquation(equation);
                var (varName, isDifferential) = ParseLeftSide(leftSide);

                // 중복 변수 체크
                if (variables.ContainsKey(varName))
                {
                    _loggingService.Error($"Variable {varName} appears in multiple equations.");
                    throw new Exception($"Variable {varName} appears in multiple equations");
                }

                // 변수 등록
                variables[varName] = variables.Count;
                _expressionParser.AddVariable(varName);

                var tokens = _expressionParser.Tokenize(rightSide);
                parsedEquations.Add(new DAEEquation
                {
                    Variable = varName,
                    IsDifferential = isDifferential,
                    Expression = rightSide,
                    TokenizedExpression = tokens
                });

                _loggingService.Debug($"Parsed Equation: Variable={varName}, IsDifferential={isDifferential}, Expression={rightSide}");
            }

            ValidateSystem(variables, parsedEquations);

            _loggingService.Info("Equation parsing completed.");
            return (CreateDAESystem(parsedEquations, variables), variables.Count);
        }

        private DAESystem CreateDAESystem(List<DAEEquation> equations, Dictionary<string, int> variables)
        {
            _loggingService.Info("Creating DAE system...");
            foreach (var eq in equations)
            {
                _loggingService.Debug($"DAE Equation: Variable={eq.Variable}, IsDifferential={eq.IsDifferential}, Expression={eq.Expression}");
            }

            return (double t, double[] y, double[] yprime) =>
            {
                double[] residuals = new double[variables.Count];

                for (int i = 0; i < equations.Count; i++)
                {
                    if (equations[i].IsDifferential)
                    {
                        var rhsValue = _expressionParser.EvaluateTokens(equations[i].TokenizedExpression, t, y);
                        var varIndex = variables[equations[i].Variable];
                        residuals[i] = yprime[varIndex] - rhsValue;

                        _loggingService.Debug($"Evaluating Differential Equation: {equations[i].Variable}, Residual={residuals[i]:E3}");
                    }
                    else
                    {
                        residuals[i] = _expressionParser.EvaluateTokens(equations[i].TokenizedExpression, t, y);
                        _loggingService.Debug($"Evaluating Algebraic Equation: {equations[i].Variable}, Residual={residuals[i]:E3}");
                    }
                }

                return residuals;
            };
        }

        private string RemoveComments(string equation)
        {
            if (string.IsNullOrWhiteSpace(equation))
                return string.Empty;

            // 공백 제거 및 트림 처리
            equation = equation.Trim();

            // 한 줄 주석 처리 (//)
            int singleLineIndex = equation.IndexOf("//", StringComparison.Ordinal);
            if (singleLineIndex >= 0)
            {
                string beforeComment = equation.Substring(0, singleLineIndex).Trim();
                return string.IsNullOrWhiteSpace(beforeComment) ? string.Empty : beforeComment;
            }

            // Python 스타일 주석 처리 (#)
            int pythonStyleIndex = equation.IndexOf("#", StringComparison.Ordinal);
            if (pythonStyleIndex >= 0)
            {
                string beforeComment = equation.Substring(0, pythonStyleIndex).Trim();
                return string.IsNullOrWhiteSpace(beforeComment) ? string.Empty : beforeComment;
            }

            // 여러 줄 주석 처리
            if (_isInMultilineComment)
            {
                int endIndex = equation.IndexOf("*/", StringComparison.Ordinal);
                if (endIndex == -1)
                    return string.Empty;

                _isInMultilineComment = false;
                equation = equation.Substring(endIndex + 2).Trim();
                return string.IsNullOrWhiteSpace(equation) ? string.Empty : equation;
            }

            while (true)
            {
                int startIndex = equation.IndexOf("/*", StringComparison.Ordinal);
                if (startIndex == -1)
                    break;

                int endIndex = equation.IndexOf("*/", startIndex, StringComparison.Ordinal);
                if (endIndex == -1)
                {
                    _isInMultilineComment = true;
                    equation = equation.Substring(0, startIndex).Trim();
                    return string.IsNullOrWhiteSpace(equation) ? string.Empty : equation;
                }

                equation = equation.Remove(startIndex, endIndex - startIndex + 2).Trim();
                if (string.IsNullOrWhiteSpace(equation))
                    return string.Empty;
            }

            return equation;
        }

        private (string leftSide, string rightSide) SplitEquation(string equation)
        {
            var parts = equation.Split('=')
                .Select(p => p.Trim())
                .ToArray();

            if (parts.Length != 2)
                throw new Exception($"Invalid equation format: {equation}");

            return (parts[0], parts[1]);
        }

        private (string varName, bool isDifferential) ParseLeftSide(string leftSide)
        {
            // 기존 코드
            var derPattern = new Regex(@"der\(([a-zA-Z][a-zA-Z0-9]*)\)");
            var match = derPattern.Match(leftSide);

            if (match.Success)
            {
                string varName = match.Groups[1].Value;
                if (_parameters.Contains(varName))
                    throw new Exception($"Cannot use parameter {varName} as variable");
                return (varName, true);
            }

            // 변수 이름 패턴을 더 엄격하게 수정
            if (!Regex.IsMatch(leftSide, @"^[a-zA-Z][a-zA-Z0-9]*$"))
            {
                throw new Exception($"Invalid variable name: {leftSide}. Variable names must start with a letter and can only contain letters and numbers");
            }
            if (_parameters.Contains(leftSide))
                throw new Exception($"Cannot use parameter {leftSide} as variable");

            return (leftSide, false);
        }

        private void ValidateSystem(Dictionary<string, int> variables, List<DAEEquation> equations)
        {
            // 실제 변수들만 추출 (주석이나 공백이 아닌 실제 방정식에서 사용된 변수들)
            var actualVariables = equations
                .Where(eq => !string.IsNullOrWhiteSpace(eq.Variable))
                .Select(e => e.Variable)
                .ToHashSet();

            if (equations.Count != actualVariables.Count)
            {
                _loggingService.Error($"Number of valid equations ({equations.Count}) does not match number of variables ({actualVariables.Count})");
                throw new Exception(
                    $"Number of equations ({equations.Count}) does not match number of variables ({actualVariables.Count})");
            }

            var undefinedVariables = actualVariables.Where(v => !variables.ContainsKey(v));

            if (undefinedVariables.Any())
            {
                var missingVars = string.Join(", ", undefinedVariables);
                _loggingService.Error($"Missing equations for variables: {missingVars}");
                throw new Exception($"Missing equations for variables: {missingVars}");
            }
        }

        public double[] ParseInitialConditions(List<string> conditions)
        {
            _loggingService.Info("Parsing initial conditions...");
            var variables = _expressionParser.GetVariables();
            if (variables.Count == 0)
            {
                _loggingService.Error("No variables defined. Parse equations first.");
                throw new Exception("No variables defined. Parse equations first.");
            }

            var initialValues = new double[variables.Count];
            var processedVariables = new HashSet<string>();


            foreach (var condition in conditions)
            {
                var (varName, value) = ParseInitialCondition(condition);

                if (!variables.ContainsKey(varName))
                {
                    _loggingService.Error($"Unknown variable in initial conditions: {varName}");
                    throw new Exception($"Unknown variable in initial conditions: {varName}");
                }
                if (!processedVariables.Add(varName))
                {
                    _loggingService.Error($"Duplicate initial condition for variable: {varName}");
                    throw new Exception($"Duplicate initial condition for variable: {varName}");
                }

                initialValues[variables[varName]] = value;
                _loggingService.Debug($"Initial Condition: {varName} = {value}");
            }

            var missingVariables = variables.Keys.Where(v => !processedVariables.Contains(v));
            if (missingVariables.Any())
            {
                var missingVars = string.Join(", ", missingVariables);
                _loggingService.Error($"Missing initial conditions for variables: {missingVars}");
                throw new Exception($"Missing initial conditions for variables: {missingVars}");
            }

            _loggingService.Info("Initial conditions parsing completed.");
            return initialValues;
        }

        private (string varName, double value) ParseInitialCondition(string condition)
        {
            var parts = condition.Split('=')
                .Select(p => p.Trim())
                .ToArray();

            if (parts.Length != 2)
                throw new Exception($"Invalid initial condition format: {condition}");

            string varName = parts[0];
            if (!double.TryParse(parts[1], out double value))
                throw new Exception($"Invalid number format in initial condition: {parts[1]}");

            return (varName, value);
        }
    }
}