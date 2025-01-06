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

        public ImplicitEulerSolver() : base("Implicit Euler")
        {
        }

        public override void Initialize(Dictionary<string, double> parameters)
        {
            base.Initialize(parameters);
        }

        public override async Task<Solution> SolveAsync(CancellationToken cancellationToken = default)
        {
            ValidateInputs();
            ValidateEquationSetup(); // DAE 또는 ODE 설정 확인

            var solution = new Solution();
            double dt = (EndTime - StartTime) / Intervals;
            double[] currentState = (double[])InitialState.Clone();
            double[] currentDerivatives = new double[Dimension];
            double currentTime = StartTime;

            while (currentTime <= EndTime)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 일시정지 체크
                if (IsPaused)
                {
                    await _pauseCompletionSource.Task;
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException();
                }

                currentState = await SolveNextStateDAEAsync(currentState, currentDerivatives, currentTime, dt, cancellationToken);
                currentTime += dt;

                solution.LogStep(currentTime, currentState, currentDerivatives);

                RaiseProgressChanged(currentTime, EndTime,
                    $"Time: {currentTime:F3}/{EndTime:F3}, Step size: {dt:E3}");

                await Task.Yield();
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

        private async Task<double[]> SolveNextStateDAEAsync(
    double[] currentState, double[] currentDerivatives, double t, double dt, CancellationToken cancellationToken)
        {
            int n = currentState.Length;
            double[] nextState = (double[])currentState.Clone();
            double[] nextDerivatives = (double[])currentDerivatives.Clone();

            for (int iter = 0; iter < MAX_ITERATIONS; iter++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double[] residuals = DAESystem(t + dt, nextState, nextDerivatives);

                if (NormL2(residuals) < TOLERANCE)
                    break;

                double[,] jacobian = await CalculateJacobianAsync(nextState, nextDerivatives, t + dt, dt, cancellationToken);
                double[] delta = SolveLinearSystem(jacobian, residuals);

                for (int i = 0; i < n; i++)
                {
                    nextState[i] -= delta[i];
                    nextDerivatives[i] = (nextState[i] - currentState[i]) / dt; // DAE를 위한 도함수 갱신
                }
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

        private async Task<double[,]> CalculateJacobianAsync(
    double[] state, double[] derivatives, double time, double dt, CancellationToken cancellationToken)
        {
            int n = state.Length;
            double[,] jacobian = new double[n, n];
            double epsilon = Math.Sqrt(TOLERANCE);

            double[] baseResiduals = DAESystem(time, state, derivatives); // 현재 잔차 계산

            for (int i = 0; i < n; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // ∂F/∂y: 상태 변화를 통한 Jacobian 요소 계산
                double[] perturbedState = (double[])state.Clone();
                perturbedState[i] += epsilon;
                double[] perturbedResidualsState = DAESystem(time, perturbedState, derivatives);

                // ∂F/∂y': 도함수 변화를 통한 Jacobian 요소 계산
                double[] perturbedDerivatives = (double[])derivatives.Clone();
                perturbedDerivatives[i] += epsilon / dt;
                double[] perturbedResidualsDerivatives = DAESystem(time, state, perturbedDerivatives);

                for (int j = 0; j < n; j++)
                {
                    jacobian[j, i] = (perturbedResidualsState[j] - baseResiduals[j]) / epsilon // ∂F/∂y
                                   + (perturbedResidualsDerivatives[j] - baseResiduals[j]) / (epsilon / dt); // ∂F/∂y'
                }

                await Task.Yield(); // 비동기 작업에서 CPU 양보
            }

            return jacobian;
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