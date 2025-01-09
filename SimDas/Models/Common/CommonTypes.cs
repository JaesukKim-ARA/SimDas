using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
using SimDas.Parser;

namespace SimDas.Models.Common
{
    public enum SolverType
    {
        ExplicitEuler,
        ImplicitEuler,
        RungeKutta4,
        DASSL
    }

    public class Solution
    {
        public List<double> TimePoints { get; set; } = new();
        public List<double[]> States { get; set; } = new();
        public List<double[]> Derivatives { get; set; } = new();

        public void LogStep(double time, double[] states, double[] derivatives)
        {
            TimePoints.Add(time);
            States.Add((double[])states.Clone());
            Derivatives.Add((double[])derivatives.Clone());
        }
    }

    public class CodeLine
    {
        public string Text { get; set; }
        public bool IsComment => Text.TrimStart().StartsWith("//") || Text.TrimStart().StartsWith("#");
    }

    public class PlotSettings
    {
        public string Title { get; set; }
        public string XLabel { get; set; }
        public string YLabel { get; set; }

        public PlotSettings(string title, string xLabel, string yLabel)
        {
            Title = title;
            XLabel = xLabel;
            YLabel = yLabel;
        }
    }

    public class ProgressEventArgs : EventArgs
    {
        public double CurrentTime { get; }
        public double EndTime { get; }
        public double ProgressPercentage => (CurrentTime / EndTime) * 100;
        public string Status { get; }

        public ProgressEventArgs(double currentTime, double endTime, string status)
        {
            CurrentTime = currentTime;
            EndTime = endTime;
            Status = status;
        }
    }

    public delegate double[] ODESystem(double t, double[] y);
    public delegate double[] DAESystem(double t, double[] y, double[] yprime);
    public class DAEEquation
    {
        public string Variable { get; set; }
        public bool IsDifferential { get; set; }
        public string Expression { get; set; }
        public Token[] TokenizedExpression { get; set; }
    }

    public class EvaluationContext
    {
        public double Time { get; set; }
        public double[] State { get; set; }
        public double[] Derivatives { get; set; }
    }

    public class ErrorAnalysis
    {
        public double MaxAbsoluteError { get; set; }
        public double MeanAbsoluteError { get; set; }
        public double RootMeanSquareError { get; set; }
        public List<double> LocalErrors { get; set; }
        public Dictionary<string, double> VariableErrors { get; set; }
        public double RelativeError { get; set; }
        public double NormalizedError { get; set; }

        public ErrorAnalysis()
        {
            LocalErrors = new List<double>();
            VariableErrors = new Dictionary<string, double>();
        }

        public void CalculateErrors(Solution solution, Solution referenceSolution)
        {
            if (solution.TimePoints.Count != referenceSolution.TimePoints.Count)
                throw new ArgumentException("Solutions must have the same number of time points");

            int n = solution.TimePoints.Count;
            double sumSquaredError = 0;
            double sumAbsoluteError = 0;
            MaxAbsoluteError = 0;

            for (int i = 0; i < n; i++)
            {
                // 각 시점에서의 오차 계산
                double localError = 0;
                for (int j = 0; j < solution.States[i].Length; j++)
                {
                    double error = Math.Abs(solution.States[i][j] - referenceSolution.States[i][j]);
                    localError += error;
                    sumSquaredError += error * error;
                    sumAbsoluteError += error;
                    MaxAbsoluteError = Math.Max(MaxAbsoluteError, error);
                }

                localError /= solution.States[i].Length;  // 평균
                LocalErrors.Add(localError);
            }

            // 전체 오차 통계 계산
            MeanAbsoluteError = sumAbsoluteError / (n * solution.States[0].Length);
            RootMeanSquareError = Math.Sqrt(sumSquaredError / (n * solution.States[0].Length));

            // 각 변수별 오차 계산
            for (int j = 0; j < solution.States[0].Length; j++)
            {
                double varError = 0;
                for (int i = 0; i < n; i++)
                {
                    varError += Math.Abs(solution.States[i][j] - referenceSolution.States[i][j]);
                }
                varError /= n;
                VariableErrors[$"Variable_{j}"] = varError;
            }

            // 상대 오차 계산
            double refNorm = 0;
            double errorNorm = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < solution.States[i].Length; j++)
                {
                    refNorm += referenceSolution.States[i][j] * referenceSolution.States[i][j];
                    double diff = solution.States[i][j] - referenceSolution.States[i][j];
                    errorNorm += diff * diff;
                }
            }

            RelativeError = Math.Sqrt(errorNorm / refNorm);
            NormalizedError = Math.Sqrt(errorNorm / (n * solution.States[0].Length));
        }

        public override string ToString()
        {
            var report = new StringBuilder();
            report.AppendLine("Error Analysis Report");
            report.AppendLine("====================");
            report.AppendLine($"Maximum Absolute Error: {MaxAbsoluteError:E6}");
            report.AppendLine($"Mean Absolute Error: {MeanAbsoluteError:E6}");
            report.AppendLine($"Root Mean Square Error: {RootMeanSquareError:E6}");
            report.AppendLine($"Relative Error: {RelativeError:E6}");
            report.AppendLine($"Normalized Error: {NormalizedError:E6}");
            report.AppendLine("\nVariable-wise Errors:");
            foreach (var error in VariableErrors)
            {
                report.AppendLine($"{error.Key}: {error.Value:E6}");
            }
            return report.ToString();
        }

        public void SaveToFile(string filename)
        {
            using var writer = new StreamWriter(filename);
            writer.WriteLine(ToString());
            writer.WriteLine("\nLocal Errors over Time:");
            for (int i = 0; i < LocalErrors.Count; i++)
            {
                writer.WriteLine($"t_{i}: {LocalErrors[i]:E6}");
            }
        }
    }

    public class AnalysisProgress
    {
        public string Stage { get; set; }
        public double Percentage { get; set; }
        public string Message { get; set; }
    }
}