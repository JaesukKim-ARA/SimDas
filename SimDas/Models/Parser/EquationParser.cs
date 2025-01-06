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
        private readonly HashSet<string> _knownFunctions;
        private readonly List<(string name, bool isDifferential)> _variableTypes;
        private readonly HashSet<string> _parameters;
        private readonly ILoggingService _loggingService;

        public EquationParser(ILoggingService loggingService)
        {
            _loggingService = loggingService;
            _expressionParser = new ExpressionParser();
            _knownFunctions = new HashSet<string> { "der", "sin", "cos", "exp", "sqrt", "tan" };
            _variableTypes = new List<(string, bool)>();
            _parameters = new HashSet<string>();
        }

        public ExpressionParser GetExpressionParser() => _expressionParser;

        public void SetParameters(Dictionary<string, double> parameters)
        {
            _loggingService.Debug("Setting parameters...");
            _parameters.Clear();
            foreach (var param in parameters)
            {
                _parameters.Add(param.Key);
            }
            _expressionParser.SetParameters(parameters);
            _loggingService.Debug($"Parameters set successfully: {string.Join(", ", parameters.Keys)}");
        }

        public void Reset()
        {
            _loggingService.Info("Resetting equation parser...");
            _expressionParser.Reset();
            _variableTypes.Clear();
            _parameters.Clear();
            _loggingService.Info("Equation parser reset completed");
        }

        public List<string> GetVariableNames() => _expressionParser.GetVariables()
            .OrderBy(v => v.Value)
            .Select(v => v.Key)
            .ToList();

        public (ODESystem equation, int dimension) Parse(List<string> equations)
        {
            // 초기화
            _variableTypes.Clear();
            var variables = new Dictionary<string, int>();
            var variableOrder = new List<string>();

            // 각 방정식 분석
            var processedEquations = ParseEquations(equations, variables, variableOrder).ToList();

            // 방정식 시스템 검증
            ValidateEquationSystem(variables, processedEquations);

            // 방정식 순서 정렬
            var orderedEquations = OrderEquations(processedEquations, variables);

            return (CreateEquationSystem(orderedEquations), variables.Count);
        }

        public (DAESystem daeSystem, int dimension) ParseDAE(List<string> equations)
        {
            // 초기화
            _variableTypes.Clear();
            var variables = new Dictionary<string, int>();
            var variableOrder = new List<string>();

            // 각 방정식 분석
            var processedEquations = ParseEquations(equations, variables, variableOrder).ToList();

            // 방정식 시스템 검증
            ValidateEquationSystem(variables, processedEquations);

            // 방정식 순서 정렬
            var orderedEquations = OrderEquations(processedEquations, variables);

            return (CreateDAESystem(orderedEquations, variables), variables.Count);
        }

        private DAESystem CreateDAESystem(string[] orderedEquations, Dictionary<string, int> variables)
        {
            return (double t, double[] y, double[] yprime) =>
            {
                var residuals = new double[orderedEquations.Length];

                for (int i = 0; i < orderedEquations.Length; i++)
                {
                    var equation = orderedEquations[i];
                    if (variables.ContainsKey(equation))
                    {
                        var variableIndex = variables[equation];

                        if (equation.StartsWith("der(") && equation.EndsWith(")"))
                        {
                            // 미분 방정식 처리
                            residuals[i] = EvaluateEquation(equation, t, y, yprime);
                        }
                        else
                        {
                            // 대수 방정식 처리
                            residuals[i] = EvaluateEquation(equation, t, y, null);
                        }
                    }
                }

                return residuals;
            };
        }

        private double EvaluateEquation(string equation, double t, double[] y, double[] yprime)
        {
            return _expressionParser.EvaluateTokens(_expressionParser.Tokenize(equation), t, y, yprime);
        }


        private IEnumerable<(string variable, string expression, bool isDifferential)>
        ParseEquations(List<string> equations, Dictionary<string, int> variables, List<string> variableOrder)
        {
            foreach (var equation in equations)
            {
                // 방정식 전처리
                var (leftSide, rightSide) = SplitEquation(equation);
                var (varName, isDifferential) = ParseLeftSide(leftSide);

                // 변수 추가
                if (!variables.ContainsKey(varName))
                {
                    variables[varName] = variables.Count;
                    variableOrder.Add(varName);
                }
                else
                {
                    throw new Exception($"Variable {varName} appears in multiple equations");
                }

                // 변수 타입 기록
                _variableTypes.Add((varName, isDifferential));

                // 유효한 변수 목록에 추가
                _expressionParser.AddVariable(varName);

                yield return (varName, rightSide, isDifferential);
            }
        }

        private (string leftSide, string rightSide) SplitEquation(string equation)
        {
            var parts = equation.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(p => p.Trim())
                                .ToArray();
            if (parts.Length != 2)
                throw new Exception($"Invalid equation format: {equation}");

            return (parts[0], parts[1]);
        }

        private (string varName, bool isDifferential) ParseLeftSide(string leftSide)
        {
            var derPattern = new Regex(@"der\(([a-zA-Z][a-zA-Z0-9]*)\)");
            var match = derPattern.Match(leftSide);

            if (match.Success)
            {
                string varName = match.Groups[1].Value;
                if (_parameters.Contains(varName))
                    throw new Exception($"Cannot use parameter {varName} as variable");
                return (varName, true);
            }
            else
            {
                if (!Regex.IsMatch(leftSide, @"^[a-zA-Z][a-zA-Z0-9]*$"))
                    throw new Exception($"Invalid variable name: {leftSide}");
                if (_parameters.Contains(leftSide))
                    throw new Exception($"Cannot use parameter {leftSide} as variable");
                return (leftSide, false);
            }
        }

        private void ValidateEquationSystem(
            Dictionary<string, int> variables,
            List<(string variable, string expression, bool isDifferential)> equations)
        {
            // 방정식 수와 변수 수가 일치하는지 확인
            if (equations.Count != variables.Count)
            {
                var error = $"Number of equations ({equations.Count}) does not match number of variables ({variables.Count})";
                _loggingService.Error(error);
                throw new Exception(error);
            }

            // 각 변수에 대한 방정식이 있는지 확인
            var definedVariables = equations.Select(e => e.variable).ToHashSet();
            var undefinedVariables = variables.Keys.Where(v => !definedVariables.Contains(v));

            if (undefinedVariables.Any())
            {
                var error = $"Missing equations for variables: {string.Join(", ", undefinedVariables)}";
                _loggingService.Error(error);
                throw new Exception(error);
            }

            _loggingService.Debug($"Equation system validation passed\n{string.Join("\n", equations)}");
        }

        private string[] OrderEquations(
            List<(string variable, string expression, bool isDifferential)> equations,
            Dictionary<string, int> variables)
        {
            var orderedEquations = new string[variables.Count];

            foreach (var (variable, expression, _) in equations)
            {
                int index = variables[variable];
                orderedEquations[index] = expression;
            }

            return orderedEquations;
        }

        private ODESystem CreateEquationSystem(string[] orderedEquations)
        {
            // 각 방정식을 토큰화
            var tokenizedEquations = orderedEquations
                .Select(eq => _expressionParser.Tokenize(eq))
                .ToArray();

            // 방정식 시스템 생성
            return (t, y) =>
            {
                var results = new double[tokenizedEquations.Length];
                for (int i = 0; i < tokenizedEquations.Length; i++)
                {
                    results[i] = _expressionParser.EvaluateTokens(tokenizedEquations[i], t, y);
                }
                return results;
            };
        }

        public double[] ParseInitialConditions(List<string> conditions)
        {
            var variables = _expressionParser.GetVariables();
            if (variables.Count == 0)
                throw new Exception("No variables defined. Parse equations first.");

            var initialValues = new double[variables.Count];
            var processedVariables = new HashSet<string>();

            // 각 초기조건 파싱
            foreach (var condition in conditions)
            {
                var (varName, value) = ParseInitialCondition(condition);

                if (!variables.ContainsKey(varName))
                    throw new Exception($"Unknown variable in initial conditions: {varName}");

                if (!processedVariables.Add(varName))
                    throw new Exception($"Duplicate initial condition for variable: {varName}");

                initialValues[variables[varName]] = value;
            }

            // 모든 변수에 대한 초기값이 제공되었는지 확인
            var missingVariables = variables.Keys.Where(v => !processedVariables.Contains(v));
            if (missingVariables.Any())
            {
                throw new Exception(
                    $"Missing initial conditions for variables: {string.Join(", ", missingVariables)}");
            }

            return initialValues;
        }

        public (string varName, double value) ParseInitialCondition(string condition)
        {
            var parts = condition.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(p => p.Trim())
                                .ToArray();
            if (parts.Length != 2)
            {
                var error = $"Invalid initial condition format: {condition}";
                _loggingService.Error(error);
                throw new Exception(error);
            }

            string varName = parts[0];
            if (!double.TryParse(parts[1], out double value))
            {
                var error = $"Invalid number format in initial condition: {parts[1]}";
                _loggingService.Error(error);
                throw new Exception(error);
            }

            _loggingService.Debug($"Parsed initial condition: {varName} = {value}");
            return (varName, value);
        }
    }
}
