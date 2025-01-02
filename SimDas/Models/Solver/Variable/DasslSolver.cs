using SimDas.Models.Common;
using SimDas.Models.Solver.Base;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace SimDas.Models.Solver.Variable
{
    public class DasslSolver : SolverBase
    {
        private const int MAX_ORDER = 5;  // BDF 최대 차수
        private int _maxNewtonIterations = 10;
        private double _relativeTolerance = 1e-6;
        private double _absoluteTolerance = 1e-8;
        private double _initialStepSize = 1e-4;
        private const double UROUND = 2.22e-16;
        private const double MIN_STEP_SIZE = 1e-10;
        private const double MAX_STEP_SIZE = 1e-2;
        private const double SAFETY_FACTOR = 0.6;

        private double[][] _phi;  // Nordsieck history array
        private int _currentOrder;
        private double _currentStepSize;
        private double[] _weights;
        private readonly int[] _factorials = { 1, 1, 2, 6, 24, 120, 720 };
        private int[] _variableTypes;  // 0: 미분 변수, 1: 대수 변수
        private double[] _nominalValues;
        private readonly double[][] _bdfCoeffsCache;

        public DasslSolver() : base("DASSL", false)
        {
            _bdfCoeffsCache = new double[][]
            {
                new double[] { 1.0, -1.0 },                                          // 1차 BDF
                new double[] { 3.0/2.0, -2.0, 1.0/2.0 },                            // 2차 BDF
                new double[] { 11.0/6.0, -3.0, 3.0/2.0, -1.0/3.0 },                // 3차 BDF
                new double[] { 25.0/12.0, -4.0, 3.0, -4.0/3.0, 1.0/4.0 },          // 4차 BDF
                new double[] { 137.0/60.0, -5.0, 5.0, -10.0/3.0, 5.0/4.0, -1.0/5.0 } // 5차 BDF
            };
        }

        public override void Initialize(Dictionary<string, double> parameters)
        {
            base.Initialize(parameters);

            _phi = new double[InitialState.Length][];
            for (int i = 0; i < InitialState.Length; i++)
            {
                _phi[i] = new double[MAX_ORDER + 1];
            }
            _weights = new double[InitialState.Length];

            _currentOrder = 1;
            _currentStepSize = _initialStepSize;
            _variableTypes = DetermineVariableTypes();
            _nominalValues = new double[InitialState.Length];

            InitializeHistoryArrays();
        }

        private void InitializeHistoryArrays()
        {
            for (int i = 0; i < InitialState.Length; i++)
            {
                _phi[i][0] = InitialState[i];  // 현재 상태
                for (int j = 1; j <= MAX_ORDER; j++)
                {
                    _phi[i][j] = 0.0;  // 높은 차수 도함수는 0으로 초기화
                }
                _nominalValues[i] = Math.Max(Math.Abs(InitialState[i]), 1e-6);
            }
        }

        public void SetAdvancedSettings(
            double relativeTolerance,
            double absoluteTolerance,
            int maxOrder,
            int maxNewtonIterations,
            double initialStepSize)
        {
            _relativeTolerance = relativeTolerance;
            _absoluteTolerance = absoluteTolerance;
            _maxNewtonIterations = maxNewtonIterations;
            _initialStepSize = initialStepSize;
            _currentOrder = Math.Min(maxOrder, MAX_ORDER);
        }

        private int[] DetermineVariableTypes()
        {
            int n = InitialState.Length;
            int[] types = new int[n];
            double eps = Math.Sqrt(UROUND);

            // 초기 residual 계산을 위한 임시 배열
            double[] tempState = (double[])InitialState.Clone();
            double[] tempDeriv = new double[n];
            double[] baseRes = DAESystem(StartTime, tempState, tempDeriv);

            // 각 변수에 대해 미분항 존재 여부 확인
            for (int i = 0; i < n; i++)
            {
                tempDeriv[i] += eps;
                double[] perturbedRes = DAESystem(StartTime, tempState, tempDeriv);
                tempDeriv[i] = 0.0;

                bool hasDifferentialTerm = false;
                for (int j = 0; j < n; j++)
                {
                    if (Math.Abs(perturbedRes[j] - baseRes[j]) > eps)
                    {
                        hasDifferentialTerm = true;
                        break;
                    }
                }

                types[i] = hasDifferentialTerm ? 0 : 1;
            }

            return types;
        }

        public override async Task<Solution> SolveAsync(CancellationToken cancellationToken = default)
        {
            ValidateInputs();
            var solution = new Solution();
            double[] currentState = (double[])InitialState.Clone();
            double[] derivatives = new double[InitialState.Length];
            double currentTime = StartTime;
            int consecutiveFailures = 0;
            const int MAX_CONSECUTIVE_FAILURES = 10;

            try
            {
                while (currentTime < EndTime && !cancellationToken.IsCancellationRequested)
                {
                    if (consecutiveFailures > MAX_CONSECUTIVE_FAILURES)
                    {
                        throw new Exception("Too many consecutive failures");
                    }

                    // 다음 시간 스텝 계산
                    double nextTime = Math.Min(currentTime + _currentStepSize, EndTime);

                    // 예측 단계
                    Predict(currentState, derivatives);

                    // 수정 단계
                    if (!await CorrectAsync(currentState, derivatives, nextTime, cancellationToken))
                    {
                        consecutiveFailures++;
                        _currentStepSize *= 0.5;
                        if (_currentStepSize < MIN_STEP_SIZE)
                        {
                            throw new Exception("Step size too small");
                        }
                        continue;
                    }

                    // 오차 검사
                    double error = EstimateError(currentState);
                    if (error > 1.0)
                    {
                        consecutiveFailures++;
                        _currentStepSize *= Math.Max(0.1, Math.Pow(0.5 / error, 1.0 / (_currentOrder + 1)));
                        continue;
                    }

                    // 스텝 성공
                    consecutiveFailures = 0;
                    currentTime = nextTime;
                    UpdateHistory(currentState, derivatives);
                    solution.LogStep(currentTime, currentState, derivatives);

                    // 다음 스텝 크기 결정
                    _currentStepSize = DetermineNextStepSize(error);

                    // 차수 조정
                    if (consecutiveFailures == 0 && _currentOrder < MAX_ORDER)
                    {
                        if (error < 0.5)
                        {
                            _currentOrder = Math.Min(_currentOrder + 1, MAX_ORDER);
                        }
                    }
                    else if (error > 0.9)
                    {
                        _currentOrder = Math.Max(_currentOrder - 1, 1);
                    }

                    // 진행률 업데이트
                    RaiseProgressChanged(currentTime, EndTime,
                        $"Time: {currentTime:F3}/{EndTime:F3}, Step size: {_currentStepSize:E3}, Order: {_currentOrder}");

                    await Task.Yield();
                }

                return solution;
            }
            catch (OperationCanceledException)
            {
                return solution;
            }
        }

        private void Predict(double[] state, double[] derivatives)
        {
            // Taylor 급수를 이용한 예측
            for (int i = 0; i < state.Length; i++)
            {
                if (_variableTypes[i] == 0)  // 미분 변수만
                {
                    double sum = _phi[i][0];
                    double power = _currentStepSize;
                    for (int j = 1; j <= _currentOrder; j++)
                    {
                        sum += _phi[i][j] * power / _factorials[j];
                        power *= _currentStepSize;
                    }
                    state[i] = sum;
                }
            }

            // 대수 변수는 이전 값 유지
            for (int i = 0; i < state.Length; i++)
            {
                if (_variableTypes[i] == 1)
                {
                    state[i] = _phi[i][0];
                    derivatives[i] = 0.0;
                }
            }
        }

        private async Task<bool> CorrectAsync(double[] state, double[] derivatives, double time,
            CancellationToken cancellationToken)
        {
            for (int iter = 0; iter < _maxNewtonIterations; iter++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 잔차 계산
                double[] residuals = DAESystem(time, state, derivatives);
                double normRes = CalculateNorm(residuals);

                if (normRes < _relativeTolerance)
                    return true;

                // Newton step
                double[,] J = await CalculateJacobianAsync(state, derivatives, time, cancellationToken);
                if (!SolveNewtonSystem(J, residuals, out double[] delta))
                    return false;

                // 해 갱신
                UpdateSolution(state, derivatives, delta);

                await Task.Yield();
            }

            return false;
        }

        private double CalculateNorm(double[] vector)
        {
            double sum = 0.0;
            for (int i = 0; i < vector.Length; i++)
            {
                if (_variableTypes[i] == 0)  // 미분 변수는 상대 오차
                {
                    double scale = Math.Max(Math.Abs(_phi[i][0]), _nominalValues[i]);
                    sum += (vector[i] / scale) * (vector[i] / scale);
                }
                else  // 대수 변수는 절대 오차
                {
                    sum += (vector[i] / _absoluteTolerance) * (vector[i] / _absoluteTolerance);
                }
            }
            return Math.Sqrt(sum / vector.Length);
        }

        private async Task<double[,]> CalculateJacobianAsync(double[] state, double[] derivatives,
            double time, CancellationToken cancellationToken)
        {
            int n = state.Length;
            double[,] J = new double[n, n];
            double eps = Math.Sqrt(UROUND);

            double[] baseRes = DAESystem(time, state, derivatives);

            for (int i = 0; i < n; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_variableTypes[i] == 0)  // 미분 변수
                {
                    // ∂F/∂y
                    double[] statePerturbed = (double[])state.Clone();
                    statePerturbed[i] += eps * _nominalValues[i];
                    double[] res1 = DAESystem(time, statePerturbed, derivatives);

                    // ∂F/∂y'
                    double[] derivPerturbed = (double[])derivatives.Clone();
                    derivPerturbed[i] += eps * _nominalValues[i] / _currentStepSize;
                    double[] res2 = DAESystem(time, state, derivPerturbed);

                    for (int j = 0; j < n; j++)
                    {
                        double dFdy = (res1[j] - baseRes[j]) / (eps * _nominalValues[i]);
                        double dFdyp = (res2[j] - baseRes[j]) / (eps * _nominalValues[i] / _currentStepSize);
                        J[j, i] = dFdy + _bdfCoeffsCache[_currentOrder - 1][0] * dFdyp / _currentStepSize;
                    }
                }
                else  // 대수 변수
                {
                    double[] statePerturbed = (double[])state.Clone();
                    statePerturbed[i] += eps * _nominalValues[i];
                    double[] res = DAESystem(time, statePerturbed, derivatives);

                    for (int j = 0; j < n; j++)
                    {
                        J[j, i] = (res[j] - baseRes[j]) / (eps * _nominalValues[i]);
                    }
                }

                await Task.Yield();
            }

            return J;
        }

        private bool SolveNewtonSystem(double[,] A, double[] b, out double[] x)
        {
            int n = b.Length;
            x = new double[n];

            // LU decomposition with partial pivoting
            int[] perm = new int[n];
            double[,] lu = (double[,])A.Clone();

            for (int i = 0; i < n; i++)
                perm[i] = i;

            for (int k = 0; k < n - 1; k++)
            {
                // Find pivot
                int p = k;
                double max = Math.Abs(lu[k, k]);
                for (int i = k + 1; i < n; i++)
                {
                    if (Math.Abs(lu[i, k]) > max)
                    {
                        max = Math.Abs(lu[i, k]);
                        p = i;
                    }
                }

                if (max < UROUND)
                    return false;

                if (p != k)
                {
                    for (int j = 0; j < n; j++)
                        (lu[k, j], lu[p, j]) = (lu[p, j], lu[k, j]);
                    (perm[k], perm[p]) = (perm[p], perm[k]);
                }

                for (int i = k + 1; i < n; i++)
                {
                    lu[i, k] /= lu[k, k];
                    for (int j = k + 1; j < n; j++)
                        lu[i, j] -= lu[i, k] * lu[k, j];
                }
            }

            // Forward substitution
            double[] y = new double[n];
            for (int i = 0; i < n; i++)
            {
                y[i] = b[perm[i]];
                for (int j = 0; j < i; j++)
                    y[i] -= lu[i, j] * y[j];
            }

            // Back substitution
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = i + 1; j < n; j++)
                    y[i] -= lu[i, j] * x[j];
                if (Math.Abs(lu[i, i]) < UROUND)
                    return false;
                x[i] = y[i] / lu[i, i];
            }

            return true;
        }

        private void UpdateSolution(double[] state, double[] derivatives, double[] delta)
        {
            for (int i = 0; i < state.Length; i++)
            {
                if (_variableTypes[i] == 0)  // 미분 변수
                {
                    state[i] -= delta[i];
                    // BDF를 이용한 derivatives 업데이트
                    double sum = 0.0;
                    for (int j = 0; j <= _currentOrder; j++)
                    {
                        sum += _bdfCoeffsCache[_currentOrder - 1][j] *
                              (j == 0 ? state[i] : _phi[i][j - 1]);
                    }
                    derivatives[i] = sum / _currentStepSize;
                }
                else  // 대수 변수
                {
                    // 대수 변수의 변화량 제한
                    double maxChange = Math.Max(Math.Abs(state[i]), _nominalValues[i]) * 0.1;
                    double limitedDelta = Math.Sign(delta[i]) * Math.Min(Math.Abs(delta[i]), maxChange);
                    state[i] -= limitedDelta;
                    derivatives[i] = 0.0;
                }
            }
        }

        private double EstimateError(double[] state)
        {
            double maxError = 0.0;

            for (int i = 0; i < state.Length; i++)
            {
                if (_variableTypes[i] == 0)  // 미분 변수만 체크
                {
                    double error = Math.Abs(_phi[i][_currentOrder + 1] / _nominalValues[i]);
                    maxError = Math.Max(maxError, error);
                }
            }

            return maxError / (_currentOrder + 1);
        }

        private double DetermineNextStepSize(double error)
        {
            if (error < 1e-10)
                return Math.Min(_currentStepSize * 2.0, MAX_STEP_SIZE);

            // 오차에 기반한 step size 조정
            double factor = Math.Pow(1.0 / error, 1.0 / (_currentOrder + 1));
            factor *= SAFETY_FACTOR;  // 안전 계수 적용

            // 급격한 변화 방지
            factor = Math.Min(2.0, Math.Max(0.5, factor));

            double newStepSize = _currentStepSize * factor;
            newStepSize = Math.Max(MIN_STEP_SIZE, Math.Min(MAX_STEP_SIZE, newStepSize));

            // EndTime을 넘지 않도록 조정
            if (EndTime - (_currentStepSize + newStepSize) < MIN_STEP_SIZE)
            {
                newStepSize = EndTime - _currentStepSize;
            }

            return newStepSize;
        }

        private void UpdateHistory(double[] state, double[] derivatives)
        {
            for (int i = 0; i < state.Length; i++)
            {
                // 변수 타입에 따라 처리
                if (_variableTypes[i] == 0)  // 미분 변수
                {
                    // 차분 계산
                    double[] diffs = new double[_currentOrder + 2];
                    diffs[0] = state[i];
                    diffs[1] = derivatives[i] * _currentStepSize;  // 1차 차분

                    // 고차 차분 계산
                    for (int j = 2; j <= _currentOrder + 1; j++)
                    {
                        diffs[j] = 0.0;
                        for (int k = 0; k < j; k++)
                        {
                            diffs[j] += _phi[i][k] * Math.Pow(-1, k) *
                                       _factorials[j] / (_factorials[k] * _factorials[j - k]);
                        }
                    }

                    // phi 배열 업데이트
                    for (int j = 0; j <= _currentOrder; j++)
                    {
                        _phi[i][j] = diffs[j];
                    }
                }
                else  // 대수 변수
                {
                    // 대수 변수는 현재 값만 저장
                    _phi[i][0] = state[i];
                    for (int j = 1; j <= _currentOrder; j++)
                    {
                        _phi[i][j] = 0.0;
                    }
                }
            }
        }

        public override void Cleanup()
        {
            base.Cleanup();
            _variableTypes = null;
            _nominalValues = null;
        }

        protected override void ValidateInputs()
        {
            base.ValidateInputs();

            if (DAESystem == null)
                throw new InvalidOperationException("DAE system is not initialized");

            if (_currentStepSize <= 0)
                throw new ArgumentException("Initial step size must be positive");

            if (_relativeTolerance <= 0 || _absoluteTolerance <= 0)
                throw new ArgumentException("Tolerances must be positive");
        }
    }
}