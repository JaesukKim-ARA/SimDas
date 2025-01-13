using SimDas.Models.Common;
using SimDas.Parser;
using SimDas.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Parameter = SimDas.Models.Common.Parameter;

namespace SimDas.Models.Parser
{
    public class ModelParser
    {
        private enum ModelSection
        {
            None,
            Variables,
            Parameters,
            Initial,
            Equations
        }

        private readonly ILoggingService _loggingService;
        private readonly Dictionary<string, Variable> _variables;
        private readonly Dictionary<string, Parameter> _parameters;
        private readonly List<InitialCondition> _initialConditions;
        private readonly List<Equation> _equations;
        private readonly HashSet<string> _reservedWords;

        public ModelParser(ILoggingService loggingService)
        {
            _loggingService = loggingService;
            _variables = new Dictionary<string, Variable>();
            _parameters = new Dictionary<string, Parameter>();
            _initialConditions = new List<InitialCondition>();
            _equations = new List<Equation>();
            _reservedWords = new HashSet<string> { "Real", "parameter", "initial", "equation", "der" };
        }

        public ParsedModel ParseModel(string modelInput)
        {
            Reset();
            try
            {
                // 주석 제거
                var lines = RemoveComments(modelInput);

                // 첫 번째 패스: 변수와 파라미터 선언 처리
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("Real"))
                        ParseVariableLine(line);
                    else if (line.StartsWith("parameter"))
                        ParseParameterLine(line);
                }

                // 두 번째 패스: 초기 조건과 방정식 처리
                var currentSection = ModelSection.None;
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("initial equation"))
                        currentSection = ModelSection.Initial;
                    else if (line.StartsWith("equation"))
                        currentSection = ModelSection.Equations;
                    else
                    {
                        switch (currentSection)
                        {
                            case ModelSection.Initial:
                                ParseInitialLine(line);
                                break;
                            case ModelSection.Equations:
                                ParseEquationLine(line);
                                break;
                        }
                    }
                }

                return new ParsedModel(_variables, _parameters, _initialConditions, _equations);
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Error parsing model: {ex.Message}");
                throw;
            }
        }

        private string[] RemoveComments(string input)
        {
            return input.Split('\n')
                .Select(line =>
                {
                    var commentIndex = line.IndexOf("//");
                    return commentIndex >= 0 ? line.Substring(0, commentIndex).Trim() : line.Trim();
                })
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
        }

        private void ParseVariableLine(string line)
        {
            // Real x, y, z; 형식 파싱
            var variableNames = line.TrimEnd(';')
                .Replace("Real", "")
                .Split(',')
                .Select(name => name.Trim());

            foreach (var name in variableNames)
            {
                if (_reservedWords.Contains(name))
                    throw new Exception($"Cannot use reserved word '{name}' as variable name");

                _variables.Add(name, new Variable(name, VariableType.Real));
                _loggingService.Debug($"Added variable: {name}");
            }
        }

        private void ParseParameterLine(string line)
        {
            // parameter Real k = 2.0; 형식 파싱
            var parts = line.TrimEnd(';')
                .Replace("parameter Real", "")
                .Split('=')
                .Select(p => p.Trim())
                .ToArray();

            if (parts.Length != 2)
                throw new Exception($"Invalid parameter declaration: {line}");

            var name = parts[0];
            if (!double.TryParse(parts[1], out double value))
                throw new Exception($"Invalid parameter value: {parts[1]}");

            _parameters.Add(name, new Parameter(name, value));
            _loggingService.Debug($"Added parameter: {name} = {value}");
        }

        private void ParseInitialLine(string line)
        {
            // x = 1.0; 형식 파싱
            var parts = line.TrimEnd(';')
                .Split('=')
                .Select(p => p.Trim())
                .ToArray();

            if (parts.Length != 2)
                throw new Exception($"Invalid initial condition: {line}");

            var variableName = parts[0];
            if (!_variables.ContainsKey(variableName))
                throw new Exception($"Undefined variable in initial condition: {variableName}");

            if (!double.TryParse(parts[1], out double value))
                throw new Exception($"Invalid initial value: {parts[1]}");

            _initialConditions.Add(new InitialCondition(variableName, value));
            _loggingService.Debug($"Added initial condition: {variableName} = {value}");
        }

        private void ParseEquationLine(string line)
        {
            var expression = line.TrimEnd(';');
            var variables = new HashSet<string>();

            // der() 함수 찾기
            var derPattern = new Regex(@"der\(([a-zA-Z][a-zA-Z0-9]*)\)");
            var isDifferential = derPattern.IsMatch(expression);

            // 변수 찾기
            var varPattern = new Regex(@"[a-zA-Z][a-zA-Z0-9]*");
            foreach (Match match in varPattern.Matches(expression))
            {
                var name = match.Value;
                if (!_reservedWords.Contains(name) && (_variables.ContainsKey(name) || _parameters.ContainsKey(name)))
                {
                    variables.Add(name);
                }
            }

            var equation = new Equation(
                expression,
                isDifferential ? EquationType.Differential : EquationType.Algebraic,
                variables.ToArray()
            );

            _equations.Add(equation);
            _loggingService.Debug($"Added equation: {expression} (Type: {equation.Type})");
        }

        public (DAESystem daeSystem, int dimension) CreateDAESystem()
        {
            // 변수들에 인덱스 할당
            int index = 0;
            foreach (var variable in _variables.Values)
            {
                variable.Index = index++;
            }

            var dimension = _variables.Count;

            DAESystem system = (double t, double[] y, double[] yprime) =>
            {
                var context = new EvaluationContext
                {
                    Time = t,
                    State = y,
                    Derivatives = yprime,
                    Parameters = _parameters.ToDictionary(p => p.Key, p => p.Value.Value)
                };

                double[] residuals = new double[dimension];

                foreach (var equation in _equations)
                {
                    var equationParser = new ExpressionParser();
                    foreach (var param in _parameters)
                    {
                        equationParser.AddConstant(param.Key, param.Value.Value);
                    }

                    var tokens = equationParser.Tokenize(equation.Expression);
                    double value = equationParser.EvaluateTokens(tokens, context);

                    // 방정식의 타겟 변수 찾기
                    var targetVar = equation.Variables.First();
                    residuals[_variables[targetVar].Index] = value;
                }

                return residuals;
            };

            return (system, dimension);
        }

        public void Reset()
        {
            _variables.Clear();
            _parameters.Clear();
            _initialConditions.Clear();
            _equations.Clear();
        }
    }
}
