using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SimDas.Parser
{
    public enum TokenType
    {
        Number,
        Variable,
        Operator,
        Function,
        LeftParen,
        RightParen,
        Comma
    }

    public class Token
    {
        public TokenType Type { get; set; }
        public string Value { get; set; }
        public int Precedence { get; set; }

        public Token(TokenType type, string value, int precedence = 0)
        {
            Type = type;
            Value = value;
            Precedence = precedence;
        }
    }

    public class TokenParser
    {
        private readonly HashSet<string> _knownFunctions = new()
        {
            "der", "sin", "cos", "exp", "sqrt", "tan"
        };

        public Token[] Tokenize(string expression)
        {
            // 주석 제거
            expression = RemoveComments(expression);
            if (string.IsNullOrWhiteSpace(expression))
                return Array.Empty<Token>();

            var tokens = new List<Token>();
            var tokenPattern = new Regex(
                @"\s*(?:([0-9]*\.?[0-9]+)|([a-zA-Z_][a-zA-Z0-9_]*)|(\+|\-|\*|/|\^)|(,)|(\()|(\)))\s*");
            var matches = tokenPattern.Matches(expression);

            bool expectOperand = true;  // true이면 숫자나 변수를 기대, false이면 연산자를 기대

            foreach (Match match in matches)
            {
                if (!string.IsNullOrEmpty(match.Groups[1].Value))  // Number
                {
                    tokens.Add(new Token(TokenType.Number, match.Groups[1].Value));
                    expectOperand = false;
                }
                else if (!string.IsNullOrEmpty(match.Groups[2].Value))  // Variable or Function
                {
                    string value = match.Groups[2].Value;
                    if (_knownFunctions.Contains(value))
                    {
                        tokens.Add(new Token(TokenType.Function, value));
                        expectOperand = true;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Variable, value));
                        expectOperand = false;
                    }
                }
                else if (!string.IsNullOrEmpty(match.Groups[3].Value))  // Operator
                {
                    string op = match.Groups[3].Value;
                    if (op == "-" && expectOperand)  // 단항 마이너스
                    {
                        tokens.Add(new Token(TokenType.Number, "-1"));
                        tokens.Add(new Token(TokenType.Operator, "*", 3));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Operator, op, GetPrecedence(op)));
                    }
                    expectOperand = true;
                }
                else if (!string.IsNullOrEmpty(match.Groups[4].Value))  // Comma
                {
                    tokens.Add(new Token(TokenType.Comma, ","));
                    expectOperand = true;
                }
                else if (!string.IsNullOrEmpty(match.Groups[5].Value))  // Left Parenthesis
                {
                    tokens.Add(new Token(TokenType.LeftParen, "("));
                    expectOperand = true;
                }
                else if (!string.IsNullOrEmpty(match.Groups[6].Value))  // Right Parenthesis
                {
                    tokens.Add(new Token(TokenType.RightParen, ")"));
                    expectOperand = false;
                }
            }

            return tokens.ToArray();
        }

        private string RemoveComments(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return string.Empty;

            // 한 줄 주석 처리 (//)
            int singleLineIndex = expression.IndexOf("//", StringComparison.Ordinal);
            if (singleLineIndex >= 0)
            {
                expression = expression.Substring(0, singleLineIndex);
            }

            // Python 스타일 주석 처리 (#)
            int pythonStyleIndex = expression.IndexOf("#", StringComparison.Ordinal);
            if (pythonStyleIndex >= 0)
            {
                expression = expression.Substring(0, pythonStyleIndex);
            }

            return expression.Trim();
        }

        private static int GetPrecedence(string op) => op switch
        {
            "^" => 4,
            "*" or "/" => 3,
            "+" or "-" => 2,
            _ => 0,
        };
    }
}