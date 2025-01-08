using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace SimDas.Models.Solver.Fixed
{
    public class ImplicitEulerSolver : SolverBase
    {
        private const int MAX_ITERATIONS = 100;
        private const double TOLERANCE = 1e-6;

        public ImplicitEulerSolver() : base("Implicit Euler")
        {
        }

        public override async Task<Solution> SolveAsync(CancellationToken cancellationToken = default)
        {
            ValidateInputs();
            ValidateEquationSetup();

            var solution = new Solution();
            double dt = (EndTime - StartTime) / Intervals;
            double[] currentState = (double[])InitialState.Clone();
            double[] currentDerivatives = new double[Dimension];
            double currentTime = StartTime;

            while (currentTime <= EndTime)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsPaused)
                {
                    await _pauseCompletionSource.Task;
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException();
                }

                solution.LogStep(currentTime, currentState, currentDerivatives);

                // 다음 상태 계산
                (currentState, currentDerivatives) = await SolveNextStateAsync(currentState, currentDerivatives, currentTime, dt, cancellationToken);
                currentTime += dt;

                RaiseProgressChanged(currentTime, EndTime,
                    $"Time: {currentTime:F3}/{EndTime:F3}, Step size: {dt:E3}");

                await Task.Yield();
            }

            return solution;
        }

        private async Task<(double[], double[])> SolveNextStateAsync(
            double[] currentState,
            double[] currentDerivatives,
            double time,
            double dt,
            CancellationToken cancellationToken)
        {
            double[] nextState = (double[])currentState.Clone();
            double[] nextDerivatives = (double[])currentDerivatives.Clone();
            var baseResiduals = DAESystem(time, currentState, currentDerivatives);

            // 초기 추정값 계산
            for (int i = 0; i < Dimension; i++)
            {
                nextDerivatives[i] -= baseResiduals[i];
                nextState[i] += dt * nextDerivatives[i];
            }

            // Newton-Raphson 반복
            for (int iter = 0; iter < MAX_ITERATIONS; iter++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double[] residuals = DAESystem(time + dt, nextState, nextDerivatives);
                double normRes = CalculateNorm(residuals);

                if (normRes < TOLERANCE)
                    break;

                double[,] J = await CalculateJacobianAsync(nextState, nextDerivatives, time + dt, dt, cancellationToken);
                double[] delta = SolveLinearSystem(J, residuals);

                // 해 및 도함수 갱신
                for (int i = 0; i < Dimension; i++)
                {
                    nextState[i] -= delta[i];
                    nextDerivatives[i] -= residuals[i];
                }

                await Task.Yield();
            }

            return (nextState, nextDerivatives);
        }

        private async Task<double[,]> CalculateJacobianAsync(
            double[] state, double[] derivatives, double time, double dt, CancellationToken cancellationToken)
        {
            double[,] J = new double[Dimension, Dimension];
            double eps = Math.Sqrt(TOLERANCE);

            double[] baseRes = DAESystem(time, state, derivatives);

            for (int i = 0; i < Dimension; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // ∂F/∂y 계산
                double[] perturbedState = (double[])state.Clone();
                perturbedState[i] += eps;
                double[] res1 = DAESystem(time, perturbedState, derivatives);

                // ∂F/∂y' 계산
                double[] perturbedDerivatives = (double[])derivatives.Clone();
                perturbedDerivatives[i] += eps;
                double[] res2 = DAESystem(time, state, perturbedDerivatives);

                for (int j = 0; j < Dimension; j++)
                {
                    double dFdy = (res1[j] - baseRes[j]) / eps;
                    double dFdyp = (res2[j] - baseRes[j]) / eps;
                    J[j, i] = dFdy + dFdyp / dt;
                }

                await Task.Yield();
            }

            return J;
        }

        private double CalculateNorm(double[] vector)
        {
            double sum = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                sum += vector[i] * vector[i];
            }
            return Math.Sqrt(sum / vector.Length);
        }

        private double[] SolveLinearSystem(double[,] A, double[] b)
        {
            int n = b.Length;
            double[] x = new double[n];

            // Gaussian elimination with partial pivoting
            for (int k = 0; k < n - 1; k++)
            {
                // Pivot selection
                int maxIndex = k;
                double maxValue = Math.Abs(A[k, k]);
                for (int i = k + 1; i < n; i++)
                {
                    if (Math.Abs(A[i, k]) > maxValue)
                    {
                        maxValue = Math.Abs(A[i, k]);
                        maxIndex = i;
                    }
                }

                if (maxIndex != k)
                {
                    // Swap rows
                    for (int j = k; j < n; j++)
                    {
                        (A[k, j], A[maxIndex, j]) = (A[maxIndex, j], A[k, j]);
                    }
                    (b[k], b[maxIndex]) = (b[maxIndex], b[k]);
                }

                // Elimination
                for (int i = k + 1; i < n; i++)
                {
                    double factor = A[i, k] / A[k, k];
                    for (int j = k; j < n; j++)
                    {
                        A[i, j] -= factor * A[k, j];
                    }
                    b[i] -= factor * b[k];
                }
            }

            // Back substitution
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = b[i];
                for (int j = i + 1; j < n; j++)
                {
                    sum -= A[i, j] * x[j];
                }
                x[i] = sum / A[i, i];
            }

            return x;
        }
    }
}