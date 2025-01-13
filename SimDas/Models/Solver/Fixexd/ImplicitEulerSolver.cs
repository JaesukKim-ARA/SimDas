using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SimDas.Models.Solver.Fixed
{
    public class ImplicitEulerSolver : SolverBase
    {
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
                (currentState, currentDerivatives) = await SolveImplicitStepAsync(
                    currentState, currentDerivatives, currentTime, dt, cancellationToken);

                currentTime += dt;

                RaiseProgressChanged(currentTime, EndTime,
                    $"Time: {currentTime:F3}/{EndTime:F3}, Step size: {dt:E3}");

                await Task.Yield();
            }

            return solution;
        }

        private async Task<(double[] state, double[] derivatives)> SolveImplicitStepAsync(
            double[] currentState,
            double[] currentDerivatives,
            double time,
            double dt,
            CancellationToken cancellationToken)
        {
            var nextState = (double[])currentState.Clone();
            var nextDerivatives = (double[])currentDerivatives.Clone();

            // 초기 추정값 계산
            var baseResiduals = DAESystem(time, currentState, currentDerivatives);
            for (int i = 0; i < Dimension; i++)
            {
                if (!_isAlgebraic[i])
                {
                    nextDerivatives[i] -= baseResiduals[i];
                    nextState[i] += dt * nextDerivatives[i];
                }
            }

            // 전체 시스템에 대한 Newton 반복
            int[] allIndices = Enumerable.Range(0, Dimension).ToArray();
            for (int iter = 0; iter < MAX_NEWTON_ITERATIONS; iter++)
            {
                var residuals = DAESystem(time + dt, nextState, nextDerivatives);
                double error = residuals.Select(Math.Abs).Max();

                if (error < TOLERANCE)
                    break;

                var J = await CalculateJacobianAsync(nextState, nextDerivatives, time + dt, allIndices, cancellationToken);
                var delta = SolveLinearSystem(J, residuals.Select(r => -r).ToArray());

                // 해 갱신
                for (int i = 0; i < Dimension; i++)
                {
                    nextState[i] += delta[i];
                    if (!_isAlgebraic[i])
                    {
                        nextDerivatives[i] = (nextState[i] - currentState[i]) / dt;
                    }
                }

                await Task.Yield();
            }

            return (nextState, nextDerivatives);
        }
    }
}