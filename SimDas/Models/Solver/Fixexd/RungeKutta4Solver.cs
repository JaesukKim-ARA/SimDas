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

                double[] k1 = DAESystem(currentTime, currentState, currentDerivatives);
                double[] k2 = DAESystem(currentTime + dt / 2, AddVectors(currentState, MultiplyVector(k1, dt / 2)), currentDerivatives);
                double[] k3 = DAESystem(currentTime + dt / 2, AddVectors(currentState, MultiplyVector(k2, dt / 2)), currentDerivatives);
                double[] k4 = DAESystem(currentTime + dt, AddVectors(currentState, MultiplyVector(k3, dt)), currentDerivatives);

                for (int i = 0; i < currentState.Length; i++)
                {
                    currentState[i] += dt * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]) / 6.0;
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