using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Linq;

namespace SimDas.Models.Solver.Fixed
{
    public class ExplicitEulerSolver : SolverBase
    {
        public ExplicitEulerSolver() : base("Explicit Euler")
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
            double[] nextState = new double[Dimension];
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

                // 미분 변수 업데이트
                var residuals = DAESystem(currentTime, currentState, currentDerivatives);
                for (int i = 0; i < Dimension; i++)
                {
                    if (!_isAlgebraic[i])
                    {
                        currentDerivatives[i] -= residuals[i];
                        nextState[i] = currentState[i] + dt * currentDerivatives[i];
                    }
                    else
                    {
                        nextState[i] = currentState[i];  // 초기값으로 대수 변수 추정
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
    }
}