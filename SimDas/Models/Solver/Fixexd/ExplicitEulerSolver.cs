using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace SimDas.Models.Solver.Fixed
{
    public class ExplicitEulerSolver : SolverBase
    {
        public ExplicitEulerSolver() : base("Explicit Euler")
        {
        }

        public override void Initialize(Dictionary<string, double> parameters)
        {
            base.Initialize(parameters);
        }

        public override async Task<Solution> SolveAsync(CancellationToken cancellationToken = default)
        {
            ValidateInputs();
            ValidateEquationSetup(); // DAE 확인

            var solution = new Solution();
            double dt = (EndTime - StartTime) / Intervals;
            double[] currentState = (double[])InitialState.Clone();
            double[] currentDerivatives = new double[Dimension];
            double currentTime = StartTime;

            while (currentTime <= EndTime)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 일시정지 체크 추가
                if (IsPaused)
                {
                    await _pauseCompletionSource.Task;
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException();
                }

                double[] residuals = DAESystem(currentTime, currentState, currentDerivatives);

                for (int i = 0; i < Dimension; i++)
                {
                    currentDerivatives[i] = residuals[i]; // Explicit Euler에서는 도함수 직접 갱신
                    currentState[i] += dt * currentDerivatives[i]; // 상태 갱신
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