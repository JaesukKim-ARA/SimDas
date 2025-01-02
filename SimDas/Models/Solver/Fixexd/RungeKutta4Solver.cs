using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace SimDas.Models.Solver.Fixed
{
    public class RungeKutta4Solver : SolverBase
    {
        public RungeKutta4Solver() : base("Runge-Kutta 4", true)
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

                // 현재 상태 저장
                double[] derivatives = DifferentialEquation(currentTime, currentState);
                solution.LogStep(currentTime, currentState, derivatives);

                if (currentTime >= EndTime) break;

                // RK4 계산
                double[] k1 = derivatives;
                double[] tempState = new double[currentState.Length];

                // k2 계산
                for (int i = 0; i < currentState.Length; i++)
                    tempState[i] = currentState[i] + dt * k1[i] / 2;
                double[] k2 = DifferentialEquation(currentTime + dt / 2, tempState);

                // k3 계산
                for (int i = 0; i < currentState.Length; i++)
                    tempState[i] = currentState[i] + dt * k2[i] / 2;
                double[] k3 = DifferentialEquation(currentTime + dt / 2, tempState);

                // k4 계산
                for (int i = 0; i < currentState.Length; i++)
                    tempState[i] = currentState[i] + dt * k3[i];
                double[] k4 = DifferentialEquation(currentTime + dt, tempState);

                // 다음 상태 계산
                for (int i = 0; i < currentState.Length; i++)
                {
                    currentState[i] += dt * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]) / 6;
                }

                currentTime += dt;

                // 진행률 업데이트
                RaiseProgressChanged(currentTime, EndTime,
                    $"Time: {currentTime:F3}/{EndTime:F3}, Step size: {dt:E3}");

                await Task.Yield();
            }

            return solution;
        }
    }
}