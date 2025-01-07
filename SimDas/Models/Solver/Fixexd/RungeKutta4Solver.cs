using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

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

                // RK4 각 단계 계산
                double[] k1 = DAESystem(currentTime, currentState, currentDerivatives);
                double[] k2State = new double[Dimension];
                for (int i = 0; i < Dimension; i++)
                    k2State[i] = currentState[i] + dt * k1[i] / 2;
                double[] k2 = DAESystem(currentTime + dt / 2, k2State, currentDerivatives);

                double[] k3State = new double[Dimension];
                for (int i = 0; i < Dimension; i++)
                    k3State[i] = currentState[i] + dt * k2[i] / 2;
                double[] k3 = DAESystem(currentTime + dt / 2, k3State, currentDerivatives);

                double[] k4State = new double[Dimension];
                for (int i = 0; i < Dimension; i++)
                    k4State[i] = currentState[i] + dt * k3[i];
                double[] k4 = DAESystem(currentTime + dt, k4State, currentDerivatives);

                // 상태 업데이트
                for (int i = 0; i < Dimension; i++)
                {
                    currentDerivatives[i] = (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]) / 6;
                    currentState[i] += dt * currentDerivatives[i];
                }

                solution.LogStep(currentTime, currentState, currentDerivatives);
                currentTime += dt;

                RaiseProgressChanged(currentTime, EndTime,
                    $"Time: {currentTime:F3}/{EndTime:F3}, Step size: {dt:E3}");

                await Task.Yield();
            }

            return solution;
        }
    }
}