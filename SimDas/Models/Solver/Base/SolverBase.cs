using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimDas.Models.Common;

namespace SimDas.Models.Solver.Base
{
    public abstract class SolverBase : ISolver
    {
        protected const double TOLERANCE = 1e-6;
        protected const double NEWTON_TOLERANCE = 1e-6;
        protected const int MAX_NEWTON_ITERATIONS = 10;

        private Solution _currentSolution;
        private bool _isPaused;
        protected TaskCompletionSource<bool> _pauseCompletionSource;
        protected bool[] _isAlgebraic;

        public string Name { get; protected set; }
        public int Intervals { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public double[] InitialState { get; set; }
        public bool IsPaused => _isPaused;
        public Solution CurrentSolution => _currentSolution;

        public event EventHandler<ProgressEventArgs> OnProgressChanged;

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
            _isAlgebraic = new bool[Dimension];
            IdentifyAlgebraicVariables();
        }

        protected virtual void IdentifyAlgebraicVariables()
        {
            var derivatives = new double[Dimension];
            var tempState = (double[])InitialState.Clone();
            var baseResiduals = DAESystem(StartTime, tempState, derivatives);

            for (int i = 0; i < Dimension; i++)
            {
                derivatives[i] = 1.0;
                var testResiduals = DAESystem(StartTime, tempState, derivatives);
                derivatives[i] = 0.0;

                bool hasDifferentialTerm = false;
                for (int j = 0; j < Dimension; j++)
                {
                    if (Math.Abs(testResiduals[j] - baseResiduals[j]) > TOLERANCE)
                    {
                        hasDifferentialTerm = true;
                        break;
                    }
                }
                _isAlgebraic[i] = !hasDifferentialTerm;
            }
        }

        protected virtual async Task<bool> SolveAlgebraicEquationsAsync(
            double[] state,
            double[] derivatives,
            double time,
            CancellationToken cancellationToken)
        {
            if (!_isAlgebraic.Any(x => x)) return true;

            var algebraicIndices = Enumerable.Range(0, Dimension)
                .Where(i => _isAlgebraic[i])
                .ToArray();

            for (int iter = 0; iter < MAX_NEWTON_ITERATIONS; iter++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var residuals = DAESystem(time, state, derivatives);
                double error = algebraicIndices.Sum(i => Math.Abs(residuals[i]));

                if (error < TOLERANCE)
                    return true;

                var J = await CalculateJacobianAsync(state, derivatives, time, algebraicIndices, cancellationToken);
                var dx = SolveLinearSystem(J, algebraicIndices.Select(i => -residuals[i]).ToArray());

                for (int i = 0; i < algebraicIndices.Length; i++)
                {
                    state[algebraicIndices[i]] += dx[i];
                }

                await Task.Yield();
            }

            return false;
        }

        protected virtual async Task<double[,]> CalculateJacobianAsync(
            double[] state,
            double[] derivatives,
            double time,
            int[] indices,
            CancellationToken cancellationToken)
        {
            int n = indices.Length;
            var J = new double[n, n];
            double eps = Math.Sqrt(TOLERANCE);

            var baseResiduals = DAESystem(time, state, derivatives);

            for (int i = 0; i < n; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_isAlgebraic[indices[i]])
                {
                    // 대수 변수에 대한 Jacobian
                    var perturbedState = (double[])state.Clone();
                    perturbedState[indices[i]] += eps;
                    var perturbedResiduals = DAESystem(time, perturbedState, derivatives);

                    for (int j = 0; j < n; j++)
                    {
                        J[j, i] = (perturbedResiduals[indices[j]] - baseResiduals[indices[j]]) / eps;
                    }
                }
                else
                {
                    // 미분 변수에 대한 Jacobian
                    var perturbedState = (double[])state.Clone();
                    var perturbedDerivatives = (double[])derivatives.Clone();

                    perturbedState[indices[i]] += eps;
                    var res1 = DAESystem(time, perturbedState, derivatives);

                    perturbedDerivatives[indices[i]] += eps;
                    var res2 = DAESystem(time, state, perturbedDerivatives);

                    for (int j = 0; j < n; j++)
                    {
                        double dFdy = (res1[indices[j]] - baseResiduals[indices[j]]) / eps;
                        double dFdyp = (res2[indices[j]] - baseResiduals[indices[j]]) / eps;
                        J[j, i] = dFdy + dFdyp;
                    }
                }

                await Task.Yield();
            }

            return J;
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
            if (DAESystem == null)
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

        protected double[] SolveLinearSystem(double[,] A, double[] b)
        {
            int n = b.Length;
            var x = new double[n];

            // Gaussian elimination with partial pivoting
            for (int k = 0; k < n - 1; k++)
            {
                // Pivot selection
                int maxRow = k;
                double maxValue = Math.Abs(A[k, k]);
                for (int i = k + 1; i < n; i++)
                {
                    if (Math.Abs(A[i, k]) > maxValue)
                    {
                        maxValue = Math.Abs(A[i, k]);
                        maxRow = i;
                    }
                }

                if (maxValue < NEWTON_TOLERANCE)
                    throw new Exception("Matrix is singular");

                if (maxRow != k)
                {
                    // Swap rows
                    for (int j = k; j < n; j++)
                        (A[k, j], A[maxRow, j]) = (A[maxRow, j], A[k, j]);
                    (b[k], b[maxRow]) = (b[maxRow], b[k]);
                }

                // Elimination
                for (int i = k + 1; i < n; i++)
                {
                    double factor = A[i, k] / A[k, k];
                    b[i] -= factor * b[k];
                    for (int j = k + 1; j < n; j++)
                        A[i, j] -= factor * A[k, j];
                }
            }

            // Back substitution
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = b[i];
                for (int j = i + 1; j < n; j++)
                    sum -= A[i, j] * x[j];
                x[i] = sum / A[i, i];
            }

            return x;
        }
    }
}
