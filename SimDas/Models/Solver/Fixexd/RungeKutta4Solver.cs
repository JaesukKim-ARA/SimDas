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

        // RungeKutta4Solver.cs 파일 내 SolveAsync 메서드 수정
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

                // RK4 각 단계의 residual 계산 
                var k1 = new double[Dimension];
                var k2 = new double[Dimension];
                var k3 = new double[Dimension];
                var k4 = new double[Dimension];

                // k1 계산
                var residuals = DAESystem(currentTime, currentState, currentDerivatives);
                for (int i = 0; i < Dimension; i++)
                {
                    k1[i] = dt * (currentDerivatives[i] - residuals[i]);
                }

                // k2 계산
                var halfState = new double[Dimension];
                for (int i = 0; i < Dimension; i++)
                    halfState[i] = currentState[i] + k1[i] / 2;
                residuals = DAESystem(currentTime + dt / 2, halfState, currentDerivatives);
                for (int i = 0; i < Dimension; i++)
                {
                    k2[i] = dt * (currentDerivatives[i] - residuals[i]);
                }

                // k3 계산 
                for (int i = 0; i < Dimension; i++)
                    halfState[i] = currentState[i] + k2[i] / 2;
                residuals = DAESystem(currentTime + dt / 2, halfState, currentDerivatives);
                for (int i = 0; i < Dimension; i++)
                {
                    k3[i] = dt * (currentDerivatives[i] - residuals[i]);
                }

                // k4 계산
                var endState = new double[Dimension];
                for (int i = 0; i < Dimension; i++)
                    endState[i] = currentState[i] + k3[i];
                residuals = DAESystem(currentTime + dt, endState, currentDerivatives);
                for (int i = 0; i < Dimension; i++)
                {
                    k4[i] = dt * (currentDerivatives[i] - residuals[i]);
                }

                // 상태 업데이트
                for (int i = 0; i < Dimension; i++)
                {
                    double stateIncrement = (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]) / 6;
                    currentState[i] += stateIncrement;
                    currentDerivatives[i] = stateIncrement / dt;
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