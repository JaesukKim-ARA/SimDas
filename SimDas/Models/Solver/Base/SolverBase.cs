using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimDas.Models.Common;

namespace SimDas.Models.Solver.Base
{
    public abstract class SolverBase : ISolver
    {
        public string Name { get; protected set; }
        public bool IsSteady { get; protected set; }
        public int Intervals { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public double[] InitialState { get; set; }

        public event EventHandler<ProgressEventArgs> OnProgressChanged;

        protected DifferentialEquation DifferentialEquation { get; private set; }
        protected DAESystem DAESystem { get; private set; }
        protected int Dimension { get; private set; }
        protected Dictionary<string, double> Parameters { get; private set; }
        protected bool IsDisposed { get; private set; }

        protected SolverBase(string name, bool isSteady)
        {
            Name = name;
            IsSteady = isSteady;
            IsDisposed = false;
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

            if (IsSteady && Intervals <= 0)
                throw new ArgumentException("Number of intervals must be positive for steady solvers");
        }

         public virtual void SetDifferentialEquation(DifferentialEquation equation, int dimension)
    {
        if (!IsSteady)
            throw new InvalidOperationException("DifferentialEquation cannot be set for non-steady solvers.");
        
        DifferentialEquation = equation;
        Dimension = dimension;
    }

    public virtual void SetDAESystem(DAESystem daeSystem, int dimension)
    {
        if (IsSteady)
            throw new InvalidOperationException("DAESystem cannot be set for steady solvers.");
        
        DAESystem = daeSystem;
        Dimension = dimension;
    }

    protected void ValidateEquationSetup()
    {
        if (IsSteady && DifferentialEquation == null)
            throw new InvalidOperationException("Steady solvers require a DifferentialEquation.");
        
        if (!IsSteady && DAESystem == null)
            throw new InvalidOperationException("Non-steady solvers require a DAESystem.");
    }
    }
}
