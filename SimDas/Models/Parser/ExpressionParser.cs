using SimDas.Models.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SimDas.Parser
{
    public class ExpressionParser
    {
        private readonly TokenParser _tokenParser;
        private readonly Dictionary<string, int> _variables;
        private readonly Dictionary<string, double> _parameters;
        private readonly HashSet<string> _validVariableNames;
        private const double EPSILON = 1e-10;

        public ExpressionParser()
        {
            _tokenParser = new TokenParser();
            _variables = new Dictionary<string, int>();
            _parameters = new Dictionary<string, double>();
            _validVariableNames = new HashSet<string>();
        }

        public void SetValidVariables(List<string> variables)
        {
            // 기존 변수들을 모두 제거
            _validVariableNames.Clear();
            _variables.Clear();

            // 주석이나 공백을 제외한 실제 변수 이름만 추가
            foreach (var variable in variables.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                // 주석의 일부나 특수문자가 포함된 변수명은 제외
                if (variable.Contains("//") || variable.Contains("/*") ||
                    variable.Contains("*/") || variable.Contains("#"))
                    continue;

                // 실제 변수 이름만 추가
                if (IsValidVariableName(variable))
                {
                    _validVariableNames.Add(variable);
                    if (!_parameters.ContainsKey(variable))
                    {
                        _variables[variable] = _variables.Count;
                    }
                }
            }
        }

        public void SetParameters(Dictionary<string, double> parameters)
        {
            _parameters.Clear();
            foreach (var parameter in parameters)
            {
                _parameters[parameter.Key] = parameter.Value;
            }
        }

        public void AddVariable(string name)
        {
            if (IsValidVariableName(name) && !_parameters.ContainsKey(name))
            {
                _validVariableNames.Add(name);
                if (!_variables.ContainsKey(name))
                {
                    _variables[name] = _variables.Count;
                }
            }
        }


        public Token[] Tokenize(string expression) => _tokenParser.Tokenize(expression);

        public double EvaluateTokens(Token[] tokens, EvaluationContext context)
        {
            var output = new Stack<double>();
            var operators = new Stack<Token>();

            foreach (var token in tokens)
            {
                switch (token.Type)
                {
                    case TokenType.Number:
                        output.Push(double.Parse(token.Value));
                        break;

                    case TokenType.Variable:
                        if (token.Value == "t")
                        {
                            output.Push(context.Time);
                        }
                        else if (_variables.TryGetValue(token.Value, out int index))
                        {
                            output.Push(context.State[index]);
                        }
                        else if (token.Value.StartsWith("der(") && token.Value.EndsWith(")"))
                        {
                            string varName = token.Value.Substring(4, token.Value.Length - 5);
                            if (_variables.TryGetValue(varName, out int derIndex))
                            {
                                output.Push(context.Derivatives[derIndex]);
                            }
                            else
                            {
                                throw new Exception($"Unknown derivative variable: {token.Value}");
                            }
                        }
                        else if (_parameters.TryGetValue(token.Value, out double paramValue))
                        {
                            output.Push(paramValue);
                        }
                        else
                        {
                            throw new Exception($"Unknown variable or parameter: {token.Value}");
                        }
                        break;

                    case TokenType.Operator:
                        while (operators.Count > 0 &&
                               operators.Peek().Type == TokenType.Operator &&
                               operators.Peek().Precedence >= token.Precedence)
                        {
                            ApplyOperator(operators.Pop(), output);
                        }
                        operators.Push(token);
                        break;

                    case TokenType.Function:
                        operators.Push(token);
                        break;

                    case TokenType.LeftParen:
                        operators.Push(token);
                        break;

                    case TokenType.RightParen:
                        while (operators.Count > 0 && operators.Peek().Type != TokenType.LeftParen)
                        {
                            ApplyOperator(operators.Pop(), output);
                        }

                        if (operators.Count == 0)
                        {
                            throw new Exception("Mismatched parentheses");
                        }

                        operators.Pop();  // Remove left parenthesis

                        if (operators.Count > 0 && operators.Peek().Type == TokenType.Function)
                        {
                            ApplyFunction(operators.Pop(), output);
                        }
                        break;
                }
            }

            while (operators.Count > 0)
            {
                ApplyOperator(operators.Pop(), output);
            }

            if (output.Count != 1)
            {
                throw new Exception("Invalid expression: incorrect number of operands");
            }

            return output.Pop();
        }

        public double EvaluateTokens(Token[] tokens, double t, double[] y)
        {
            return EvaluateTokens(tokens, new EvaluationContext
            {
                Time = t,
                State = y,
                Derivatives = new double[y.Length]
            });
        }

        public double EvaluateTokens(Token[] tokens, double t, double[] y, double[] yprime)
        {
            return EvaluateTokens(tokens, new EvaluationContext
            {
                Time = t,
                State = y,
                Derivatives = yprime
            });
        }

        private bool IsValidVariableName(string name)
        {
            // null이거나 빈 문자열 체크
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // 첫 문자는 반드시 알파벳이어야 함
            if (!char.IsLetter(name[0]))
                return false;

            // 두 번째 문자부터는 알파벳이나 숫자만 허용
            for (int i = 1; i < name.Length; i++)
            {
                if (!char.IsLetterOrDigit(name[i]))
                    return false;
            }

            // 예약어 체크 (der, t 등은 변수로 사용할 수 없음)
            var reservedWords = new HashSet<string> { "der", "t" };
            if (reservedWords.Contains(name))
                return false;

            return true;
        }

        private void ApplyOperator(Token op, Stack<double> output)
        {
            if (output.Count < 2)
                throw new Exception("Invalid expression: not enough operands");

            double b = output.Pop();
            double a = output.Pop();

            double result = op.Value switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => Math.Abs(b) < EPSILON ? throw new DivideByZeroException() : a / b,
                "^" => Math.Pow(a, b),
                _ => throw new Exception($"Unknown operator: {op.Value}"),
            };

            output.Push(result);
        }

        private void ApplyFunction(Token func, Stack<double> output)
        {
            if (output.Count < 1)
                throw new Exception("Invalid expression: not enough arguments for function");

            double a = output.Pop();
            double result = func.Value switch
            {
                "sin" => Math.Sin(a),
                "cos" => Math.Cos(a),
                "exp" => Math.Exp(a),
                "sqrt" => a < 0 ? throw new Exception("Cannot take square root of negative number") : Math.Sqrt(a),
                "tan" => Math.Tan(a),
                _ => throw new Exception($"Unknown function: {func.Value}"),
            };

            output.Push(result);
        }

        public Dictionary<string, int> GetVariables()
        {
            return new Dictionary<string, int>(_variables);
        }

        public void Reset()
        {
            _variables.Clear();
            _parameters.Clear();
            _validVariableNames.Clear();
        }
    }
}