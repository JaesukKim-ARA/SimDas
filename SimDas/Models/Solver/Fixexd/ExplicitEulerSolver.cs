using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace SimDas.Models.Solver.Fixed
{
    public class ExplicitEulerSolver : SolverBase
    {
        public ExplicitEulerSolver() : base("Explicit Euler", true)
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

                // 다음 상태 계산
                for (int i = 0; i < currentState.Length; i++)
                {
                    currentState[i] += dt * derivatives[i];
                }

                currentTime += dt;

                // 진행률 업데이트
                RaiseProgressChanged(currentTime, EndTime,
                    $"Time: {currentTime:F3}/{EndTime:F3}, Step size: {dt:E3}");

                // 비동기 작업 양보
                await Task.Yield();
            }

            return solution;
        }
    }
}