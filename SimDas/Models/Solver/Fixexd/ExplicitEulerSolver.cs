﻿using SimDas.Models.Common;
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

                double[] residuals = DAESystem(currentTime, currentState, currentDerivatives);

                for (int i = 0; i < Dimension; i++)
                {
                    currentDerivatives[i] -= residuals[i];
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