using System;
using System.Collections.Generic;

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
            _validVariableNames.Clear();
            foreach (var variable in variables)
            {
                _validVariableNames.Add(variable);
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
            if (!_parameters.ContainsKey(name) &&
                _validVariableNames.Contains(name) &&
                !_variables.ContainsKey(name))
            {
                _variables[name] = _variables.Count;
            }
        }

        public Token[] Tokenize(string expression) => _tokenParser.Tokenize(expression);

        public double EvaluateTokens(Token[] tokens, double t, double[] y)
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
                            output.Push(t);
                        }
                        else if (_variables.TryGetValue(token.Value, out int index))
                        {
                            output.Push(y[index]);
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

        public double EvaluateTokens(Token[] tokens, double t, double[] y, double[] yprime)
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
                            output.Push(t);
                        }
                        else if (_variables.TryGetValue(token.Value, out int index))
                        {
                            output.Push(y[index]);
                        }
                        else if (yprime != null && token.Value.StartsWith("der(") && token.Value.EndsWith(")"))
                        {
                            // Extract the variable name from "der(variable)"
                            string variableName = token.Value.Substring(4, token.Value.Length - 5);
                            if (_variables.TryGetValue(variableName, out int primeIndex))
                            {
                                output.Push(yprime[primeIndex]);
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