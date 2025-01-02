using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace SimDas.Models.Solver.Fixed
{
    public class ImplicitEulerSolver : SolverBase
    {
        private const int MAX_ITERATIONS = 100;
        private const double TOLERANCE = 1e-6;

        public ImplicitEulerSolver() : base("Implicit Euler", true)
        {
        }

        public override void Initialize(Dictionary<string, double> parameters)
        {
            base.Initialize(parameters);
        }

        public override async Task<Solution> SolveAsync(CancellationToken cancellationToken = default)
        {
            ValidateInputs();

            var solution = new Solution();
            double dt = (EndTime - StartTime) / Intervals;
            double[] currentState = (double[])InitialState.Clone();
            double currentTime = StartTime;

            while (currentTime <= EndTime)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double[] derivatives = DifferentialEquation(currentTime, currentState);
                solution.LogStep(currentTime, currentState, derivatives);

                if (currentTime >= EndTime) break;

                // Newton-Raphson 방법으로 다음 상태 계산
                double[] nextState = await SolveNextStateAsync(currentState, currentTime, dt, cancellationToken);
                currentState = nextState;
                currentTime += dt;

                // 진행률 업데이트
                RaiseProgressChanged(currentTime, EndTime,
                    $"Time: {currentTime:F3}/{EndTime:F3}, Step size: {dt:E3}");
            }

            return solution;
        }

        private async Task<double[]> SolveNextStateAsync(double[] currentState, double t, double dt,
            CancellationToken cancellationToken)
        {
            int n = currentState.Length;
            double[] guess = new double[n];
            double[] nextState = new double[n];

            // 초기 추정값으로 Explicit Euler 결과 사용
            double[] derivatives = DifferentialEquation(t, currentState);
            for (int i = 0; i < n; i++)
            {
                guess[i] = currentState[i] + dt * derivatives[i];
            }

            Array.Copy(guess, nextState, n);

            // Newton-Raphson 반복
            for (int iter = 0; iter < MAX_ITERATIONS; iter++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double[] f = DifferentialEquation(t + dt, nextState);
                double[] F = new double[n];
                for (int i = 0; i < n; i++)
                {
                    F[i] = nextState[i] - currentState[i] - dt * f[i];
                }

                // 수렴 확인
                if (NormL2(F) < TOLERANCE)
                    break;

                // Jacobian 근사 계산
                double[,] J = await CalculateJacobianAsync(nextState, t + dt, dt, cancellationToken);

                // 선형 시스템 해결
                double[] delta = SolveLinearSystem(J, F);

                // 해 갱신
                for (int i = 0; i < n; i++)
                {
                    nextState[i] -= delta[i];
                }

                await Task.Yield();
            }

            return nextState;
        }

        private async Task<double[,]> CalculateJacobianAsync(double[] state, double time, double dt,
            CancellationToken cancellationToken)
        {
            int n = state.Length;
            double[,] J = new double[n, n];
            double eps = Math.Sqrt(TOLERANCE);

            for (int i = 0; i < n; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double[] statePerturbed = (double[])state.Clone();
                statePerturbed[i] += eps;

                double[] f = DifferentialEquation(time, state);
                double[] fPerturbed = DifferentialEquation(time, statePerturbed);

                for (int j = 0; j < n; j++)
                {
                    double dfdx = (fPerturbed[j] - f[j]) / eps;
                    J[j, i] = (i == j ? 1.0 : 0.0) - dt * dfdx;
                }

                await Task.Yield();
            }

            return J;
        }

        private double NormL2(double[] v)
        {
            double sum = 0;
            for (int i = 0; i < v.Length; i++)
                sum += v[i] * v[i];
            return Math.Sqrt(sum);
        }

        private double[] SolveLinearSystem(double[,] A, double[] b)
        {
            int n = b.Length;
            double[] x = new double[n];

            // 가우스 소거법
            double[,] augmented = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    augmented[i, j] = A[i, j];
                augmented[i, n] = b[i];
            }

            for (int k = 0; k < n - 1; k++)
            {
                for (int i = k + 1; i < n; i++)
                {
                    double factor = augmented[i, k] / augmented[k, k];
                    for (int j = k; j <= n; j++)
                        augmented[i, j] -= factor * augmented[k, j];
                }
            }

            for (int i = n - 1; i >= 0; i--)
            {
                double sum = augmented[i, n];
                for (int j = i + 1; j < n; j++)
                    sum -= augmented[i, j] * x[j];
                x[i] = sum / augmented[i, i];
            }

            return x;
        }
    }
}