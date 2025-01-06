using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimDas.Models.Common;

namespace SimDas.Models.Solver.Base
{
    public abstract class SolverBase : ISolver
    {
        private Solution _currentSolution;
        private bool _isPaused;
        protected TaskCompletionSource<bool> _pauseCompletionSource;

        public string Name { get; protected set; }
        public int Intervals { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public double[] InitialState { get; set; }
        public bool IsPaused => _isPaused;
        public Solution CurrentSolution => _currentSolution;

        public event EventHandler<ProgressEventArgs> OnProgressChanged;

        protected ODESystem DifferentialEquation { get; private set; }
        protected DAESystem DAESystem { get; private set; }
        protected int Dimension { get; private set; }
        protected Dictionary<string, double> Parameters { get; private set; }
        protected bool IsDisposed { get; private set; }

        protected SolverBase(string name)
        {
            Name = name;
            IsDisposed = false;
            _currentSolution = new Solution();
            _pauseCompletionSource = new TaskCompletionSource<bool>();
        }

        public virtual void Initialize(Dictionary<string, double> parameters)
        {
            Parameters = new Dictionary<string, double>(parameters);
        }

        public abstract Task<Solution> SolveAsync(CancellationToken cancellationToken = default);

        protected virtual void RaiseProgressChanged(double currentTime, double endTime, string status)
        {
            OnProgressChanged?.Invoke(this, new ProgressEventArgs(currentTime, endTime, status));
        }

        public virtual void Cleanup()
        {
            if (!IsDisposed)
            {
                Parameters?.Clear();
                IsDisposed = true;
            }
        }

        protected virtual void ValidateInputs()
        {
            if (InitialState == null || InitialState.Length == 0)
                throw new InvalidOperationException("Initial state is not set");

            if (EndTime <= StartTime)
                throw new ArgumentException("End time must be greater than start time");

            if (Intervals <= 0)
                throw new ArgumentException("Number of intervals must be positive for steady solvers");
        }

        public virtual void SetODESystem(ODESystem equation, int dimension)
        {
            DifferentialEquation = equation;
            Dimension = dimension;
        }

        public virtual void SetDAESystem(DAESystem daeSystem, int dimension)
        {
            DAESystem = daeSystem;
            Dimension = dimension;
        }

        public virtual void Pause()
        {
            if (!_isPaused)
            {
                _isPaused = true;
                _pauseCompletionSource = new TaskCompletionSource<bool>();
            }
        }

        public virtual void Resume()
        {
            if (_isPaused)
            {
                _isPaused = false;
                _pauseCompletionSource.SetResult(true);
            }
        }

        protected virtual void ValidateEquationSetup()
        {
            if (DifferentialEquation == null && DAESystem == null)
            {
                throw new InvalidOperationException("Solver requires a DifferentialEquation or DAESystem.");
            }
        }

        protected double[] AddVectors(double[] v1, double[] v2)
        {
            double[] result = new double[v1.Length];
            for (int i = 0; i < v1.Length; i++)
            {
                result[i] = v1[i] + v2[i];
            }
            return result;
        }

        protected double[] MultiplyVector(double[] v, double scalar)
        {
            double[] result = new double[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                result[i] = v[i] * scalar;
            }
            return result;
        }
    }
}
