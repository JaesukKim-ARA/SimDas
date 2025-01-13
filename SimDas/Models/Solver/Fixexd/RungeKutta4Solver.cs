using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Linq;

namespace SimDas.Models.Solver.Fixed
{
    public class RungeKutta4Solver : SolverBase
    {
        public RungeKutta4Solver() : base("Runge-Kutta 4")
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

                // RK4 단계 계산
                var k1 = await CalculateStageAsync(currentTime, currentState, dt, 0.0, null, cancellationToken);
                var k2 = await CalculateStageAsync(currentTime + 0.5 * dt, currentState, dt, 0.5, k1, cancellationToken);
                var k3 = await CalculateStageAsync(currentTime + 0.5 * dt, currentState, dt, 0.5, k2, cancellationToken);
                var k4 = await CalculateStageAsync(currentTime + dt, currentState, dt, 1.0, k3, cancellationToken);

                // 상태 업데이트
                var nextState = new double[Dimension];
                for (int i = 0; i < Dimension; i++)
                {
                    if (!_isAlgebraic[i])
                    {
                        nextState[i] = currentState[i] +
                            (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]) / 6.0;
                        currentDerivatives[i] = (nextState[i] - currentState[i]) / dt;
                    }
                    else
                    {
                        nextState[i] = currentState[i];
                    }
                }

                // 대수 방정식 해결
                if (!await SolveAlgebraicEquationsAsync(nextState, currentDerivatives, currentTime + dt, cancellationToken))
                {
                    throw new Exception("Failed to solve algebraic equations");
                }

                Array.Copy(nextState, currentState, Dimension);
                solution.LogStep(currentTime, currentState, currentDerivatives);
                currentTime += dt;

                RaiseProgressChanged(currentTime, EndTime,
                    $"Time: {currentTime:F3}/{EndTime:F3}, Step size: {dt:E3}");

                await Task.Yield();
            }

            return solution;
        }

        private async Task<double[]> CalculateStageAsync(
            double time,
            double[] baseState,
            double dt,
            double alpha,
            double[] previousK,
            CancellationToken cancellationToken)
        {
            var stageState = new double[Dimension];
            var stageDerivatives = new double[Dimension];

            // 스테이지 상태 계산
            for (int i = 0; i < Dimension; i++)
            {
                if (!_isAlgebraic[i])
                {
                    stageState[i] = baseState[i];
                    if (previousK != null)
                    {
                        stageState[i] += dt * alpha * previousK[i];
                    }
                }
                else
                {
                    stageState[i] = baseState[i];
                }
            }

            // 대수 방정식 해결
            if (!await SolveAlgebraicEquationsAsync(stageState, stageDerivatives, time, cancellationToken))
            {
                throw new Exception("Failed to solve algebraic equations in RK stage");
            }

            // 미분항 계산
            var k = new double[Dimension];
            var residuals = DAESystem(time, stageState, stageDerivatives);
            for (int i = 0; i < Dimension; i++)
            {
                if (!_isAlgebraic[i])
                {
                    stageDerivatives[i] -= residuals[i];
                    k[i] = stageDerivatives[i];
                }
                else
                {
                    k[i] = 0;
                }
            }

            return k;
        }
    }
}