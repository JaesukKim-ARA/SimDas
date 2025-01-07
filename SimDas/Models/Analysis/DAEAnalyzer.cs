using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics;
using SimDas.Models.Common;
using SimDas.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Buffers;

namespace SimDas.Models.Analysis
{
    public class DAEAnalyzer
    {
        private readonly ILoggingService _loggingService;
        private const double PERTURBATION = 1e-6;
        private const double STIFFNESS_THRESHOLD = 1000.0;
        private readonly IProgress<AnalysisProgress> _progress;

        public DAEAnalyzer(ILoggingService loggingService, IProgress<AnalysisProgress> progress = null)
        {
            _loggingService = loggingService;
            _progress = progress;
        }

        public DAEAnalysis AnalyzeSystem(DAESystem system, int dimension, double[] initialState, double initialTime = 0.0)
        {
            var analysis = new DAEAnalysis
            {
                AlgebraicVariables = new bool[dimension],
                Warnings = new string[] { },
                IsStiff = false,
                ConditionNumber = double.NaN,
                StiffnessRatio = double.NaN
            };

            try
            {
                // 대수 변수 식별
                IdentifyAlgebraicVariables(system, initialState, initialTime, analysis);

                // Index 분석
                analysis.Index = DetermineIndex(system, initialState, initialTime, analysis.AlgebraicVariables);

                // Jacobian 계산
                var jacobian = CalculateJacobian(system, initialState, new double[dimension], initialTime);

                // 조건수 계산
                analysis.ConditionNumber = CalculateConditionNumber(jacobian);

                // Stiffness 분석 및 비율 계산
                var eigenvalues = CalculateEigenvalues(jacobian);
                analysis.IsStiff = CheckStiffness(system, initialState, initialTime);

                if (eigenvalues.Length >= 2)
                {
                    var negativeEigenvalues = eigenvalues
                        .Where(e => e.Real < 0)
                        .Select(e => Math.Abs(e.Real))
                        .ToList();

                    if (negativeEigenvalues.Count >= 2)
                    {
                        analysis.StiffnessRatio = negativeEigenvalues.Max() / negativeEigenvalues.Min();
                    }
                }

                // 경고 메시지 생성
                GenerateWarnings(analysis);
            }
            catch (Exception ex)
            {
                _loggingService.Error($"DAE analysis failed: {ex.Message}");
                throw;
            }

            return analysis;
        }

        public async Task<DAEAnalysis> AnalyzeSystemAsync(
            DAESystem system,
            int dimension,
            double[] initialState,
            double initialTime = 0.0,
            CancellationToken cancellationToken = default)
        {
            var analysis = new DAEAnalysis
            {
                AlgebraicVariables = new bool[dimension],
                Warnings = Array.Empty<string>(),
                Eigenvalues = Array.Empty<Complex32>()
            };

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 대수 변수 식별
                await IdentifyAlgebraicVariablesAsync(system, initialState, initialTime, analysis);

                cancellationToken.ThrowIfCancellationRequested();
                // Index 분석
                analysis.Index = await DetermineIndexAsync(system, initialState, initialTime, analysis.AlgebraicVariables);

                cancellationToken.ThrowIfCancellationRequested();
                // Jacobian 계산 - 병렬 처리 적용
                var jacobian = await CalculateJacobianParallelAsync(system, initialState, new double[dimension], initialTime);

                // 조건수 계산
                analysis.ConditionNumber = CalculateConditionNumber(jacobian);

                // Stiffness 분석
                analysis.IsStiff = await CheckStiffnessAsync(system, initialState, initialTime);
                analysis.Eigenvalues = CalculateEigenvalues(jacobian);

                if (analysis.Eigenvalues.Length >= 2)
                {
                    var negativeEigenvalues = analysis.Eigenvalues
                        .Where(e => e.Real < 0)
                        .Select(e => Math.Abs(e.Real))
                        .ToList();

                    if (negativeEigenvalues.Count >= 2)
                    {
                        analysis.StiffnessRatio = negativeEigenvalues.Max() / negativeEigenvalues.Min();
                    }
                }

                // 경고 메시지 생성
                GenerateWarnings(analysis);
            }
            catch (OperationCanceledException)
            {
                _loggingService.Warning("DAE analysis was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _loggingService.Error($"DAE analysis failed: {ex.Message}");
                throw;
            }

            return analysis;
        }

        private void IdentifyAlgebraicVariables(DAESystem system, double[] state, double time, DAEAnalysis analysis)
        {
            double[] derivatives = new double[state.Length];
            double[] baseResiduals = system(time, state, derivatives);

            for (int i = 0; i < state.Length; i++)
            {
                derivatives[i] += PERTURBATION;
                double[] perturbedResiduals = system(time, state, derivatives);
                derivatives[i] -= PERTURBATION;

                bool isAlgebraic = true;
                for (int j = 0; j < state.Length; j++)
                {
                    if (Math.Abs(perturbedResiduals[j] - baseResiduals[j]) > PERTURBATION)
                    {
                        isAlgebraic = false;
                        break;
                    }
                }
                analysis.AlgebraicVariables[i] = isAlgebraic;
            }
        }

        private async Task IdentifyAlgebraicVariablesAsync(DAESystem system, double[] state, double time, DAEAnalysis analysis)
        {
            var tasks = new Task[state.Length];
            for (int i = 0; i < state.Length; i++)
            {
                int index = i;  // 클로저를 위한 복사
                tasks[i] = Task.Run(() =>
                {
                    double[] derivatives = new double[state.Length];
                    double[] baseResiduals = system(time, state, derivatives);
                    derivatives[index] += PERTURBATION;
                    double[] perturbedResiduals = system(time, state, derivatives);

                    analysis.AlgebraicVariables[index] = !perturbedResiduals
                        .Zip(baseResiduals, (p, b) => Math.Abs(p - b))
                        .Any(diff => diff > PERTURBATION);
                });
            }
            await Task.WhenAll(tasks);
        }

        private int DetermineIndex(DAESystem system, double[] state, double time, bool[] algebraicVars)
        {
            int index = 1;
            double[] derivatives = new double[state.Length];
            bool[] currentAlgebraic = (bool[])algebraicVars.Clone();

            while (currentAlgebraic.Any(x => x) && index < 4)
            {
                bool[] nextAlgebraic = new bool[state.Length];
                double[] baseResiduals = system(time, state, derivatives);

                for (int i = 0; i < state.Length; i++)
                {
                    if (!currentAlgebraic[i]) continue;

                    state[i] += PERTURBATION;
                    double[] perturbedResiduals = system(time, state, derivatives);
                    state[i] -= PERTURBATION;

                    nextAlgebraic[i] = true;
                    for (int j = 0; j < state.Length; j++)
                    {
                        if (Math.Abs(perturbedResiduals[j] - baseResiduals[j]) > PERTURBATION)
                        {
                            nextAlgebraic[i] = false;
                            break;
                        }
                    }
                }

                if (!nextAlgebraic.Any(x => x)) break;
                currentAlgebraic = nextAlgebraic;
                index++;
            }

            return index;
        }

        private async Task<int> DetermineIndexAsync(DAESystem system, double[] state, double time, bool[] algebraicVars)
        {
            int index = 1;
            double[] derivatives = new double[state.Length];
            bool[] currentAlgebraic = (bool[])algebraicVars.Clone();

            while (currentAlgebraic.Any(x => x) && index < 4)
            {
                bool[] nextAlgebraic = new bool[state.Length];
                double[] baseResiduals = system(time, state, derivatives);

                var tasks = new List<Task>();
                for (int i = 0; i < state.Length; i++)
                {
                    if (!currentAlgebraic[i]) continue;

                    int localI = i;  // 클로저를 위한 로컬 변수
                    tasks.Add(Task.Run(() =>
                    {
                        var localState = (double[])state.Clone();
                        localState[localI] += PERTURBATION;
                        var perturbedResiduals = system(time, localState, derivatives);

                        nextAlgebraic[localI] = !perturbedResiduals
                            .Zip(baseResiduals, (p, b) => Math.Abs(p - b))
                            .Any(diff => diff > PERTURBATION);
                    }));
                }

                await Task.WhenAll(tasks);

                if (!nextAlgebraic.Any(x => x)) break;
                currentAlgebraic = nextAlgebraic;
                index++;
            }

            return index;
        }

        private bool CheckStiffness(DAESystem system, double[] state, double time)
        {
            try
            {
                double[] derivatives = new double[state.Length];
                var jacobian = CalculateJacobian(system, state, derivatives, time);
                var eigenvalues = CalculateEigenvalues(jacobian);

                if (!eigenvalues.Any()) return false;

                // 실수부가 음수인 eigenvalue만 고려
                var negativeEigenvalues = eigenvalues
                    .Where(e => e.Real < 0)
                    .Select(e => Math.Abs(e.Real))
                    .ToList();

                if (negativeEigenvalues.Count < 2) return false;

                double maxReal = negativeEigenvalues.Max();
                double minReal = negativeEigenvalues.Min();

                // Stiffness ratio 계산
                double stiffnessRatio = maxReal / minReal;
                _loggingService.Debug($"Stiffness ratio: {stiffnessRatio}");

                return stiffnessRatio > STIFFNESS_THRESHOLD;
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Stiffness check failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CheckStiffnessAsync(DAESystem system, double[] state, double time)
        {
            try
            {
                double[] derivatives = new double[state.Length];
                var jacobian = await CalculateJacobianParallelAsync(system, state, derivatives, time);
                var eigenvalues = CalculateEigenvalues(jacobian);

                return await Task.Run(() =>
                {
                    if (!eigenvalues.Any()) return false;

                    var negativeEigenvalues = eigenvalues
                        .Where(e => e.Real < 0)
                        .Select(e => Math.Abs(e.Real))
                        .ToList();

                    if (negativeEigenvalues.Count < 2) return false;

                    double maxReal = negativeEigenvalues.Max();
                    double minReal = negativeEigenvalues.Min();

                    double stiffnessRatio = maxReal / minReal;
                    _loggingService.Debug($"Stiffness ratio: {stiffnessRatio}");

                    return stiffnessRatio > STIFFNESS_THRESHOLD;
                });
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Stiffness check failed: {ex.Message}");
                return false;
            }
        }

        private void GenerateWarnings(DAEAnalysis analysis)
        {
            var warnings = new List<string>();

            if (analysis.Index > 2)
            {
                warnings.Add($"High index ({analysis.Index}) DAE detected. Consider index reduction.");
            }

            if (analysis.IsStiff)
            {
                warnings.Add("Stiff system detected. Implicit solvers recommended.");
            }

            int algebraicCount = analysis.AlgebraicVariables.Count(x => x);
            if (algebraicCount > 0)
            {
                warnings.Add($"System contains {algebraicCount} algebraic constraints.");
            }

            analysis.Warnings = warnings.ToArray();
        }

        private double[,] CalculateJacobian(DAESystem system, double[] state, double[] derivatives, double time)
        {
            int n = state.Length;
            var jacobian = new double[n, n];
            double[] baseResiduals = system(time, state, derivatives);

            for (int i = 0; i < n; i++)
            {
                var perturbedState = (double[])state.Clone();
                perturbedState[i] += PERTURBATION;
                double[] perturbedResiduals = system(time, perturbedState, derivatives);

                for (int j = 0; j < n; j++)
                {
                    jacobian[j, i] = (perturbedResiduals[j] - baseResiduals[j]) / PERTURBATION;
                }
            }

            return jacobian;
        }

        private async Task<double[,]> CalculateJacobianParallelAsync(
            DAESystem system,
            double[] state,
            double[] derivatives,
            double time,
            CancellationToken cancellationToken = default)
        {
            int n = state.Length;
            var jacobian = new double[n, n];

            const int MIN_BATCH_SIZE = 4;
            int batchSize = Math.Max(MIN_BATCH_SIZE, n / Environment.ProcessorCount);
            int numBatches = (n + batchSize - 1) / batchSize;

            // 기본 residual 배열 준비
            double[] baseResiduals = RentArray<double>(n);

            try
            {
                baseResiduals = system(time, state, derivatives);
                ReportProgress("Calculating Jacobian", 0, "Starting Jacobian calculation...");

                var tasks = new Task[numBatches];
                for (int batch = 0; batch < numBatches; batch++)
                {
                    int batchStart = batch * batchSize;
                    int batchEnd = Math.Min(batchStart + batchSize, n);
                    int batchIndex = batch;  // 클로저를 위한 로컬 변수

                    tasks[batch] = Task.Run(() =>
                    {
                        var perturbedState = RentArray<double>(n);
                        var perturbedDerivatives = RentArray<double>(n);
                        var tempResiduals = RentArray<double>(n);

                        try
                        {
                            for (int col = batchStart; col < batchEnd && !cancellationToken.IsCancellationRequested; col++)
                            {
                                // 진행률 보고 (각 배치의 진행상황)
                                double batchProgress = (double)(col - batchStart) / (batchEnd - batchStart);
                                double overallProgress = (batchIndex * batchSize + (col - batchStart)) * 100.0 / n;
                                ReportProgress("Calculating Jacobian", overallProgress,
                                    $"Processing batch {batchIndex + 1}/{numBatches} ({overallProgress:F1}%)");

                                Array.Copy(state, perturbedState, n);
                                Array.Copy(derivatives, perturbedDerivatives, n);

                                // 상태 변수에 대한 편미분
                                perturbedState[col] += PERTURBATION;
                                var statePerturbedResiduals = system(time, perturbedState, derivatives);

                                // 도함수에 대한 편미분
                                Array.Copy(state, perturbedState, n);
                                perturbedDerivatives[col] += PERTURBATION;
                                var derivativePerturbedResiduals = system(time, state, perturbedDerivatives);

                                for (int row = 0; row < n; row++)
                                {
                                    double dFdy = (statePerturbedResiduals[row] - baseResiduals[row]) / PERTURBATION;
                                    double dFdyp = (derivativePerturbedResiduals[row] - baseResiduals[row]) / PERTURBATION;
                                    jacobian[row, col] = dFdy + dFdyp;
                                }
                            }
                        }
                        finally
                        {
                            ReturnArray(perturbedState);
                            ReturnArray(perturbedDerivatives);
                            ReturnArray(tempResiduals);
                        }
                    }, cancellationToken);
                }

                await Task.WhenAll(tasks);
                ReportProgress("Calculating Jacobian", 100, "Jacobian calculation completed");
            }
            catch (OperationCanceledException)
            {
                _loggingService.Warning("Jacobian calculation was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Error calculating Jacobian: {ex.Message}");
                throw;
            }
            finally
            {
                //ReturnArray(baseResiduals);
            }

            return jacobian;
        }

        private Complex32[] CalculateEigenvalues(double[,] matrix)
        {
            try
            {
                // 2D 배열을 Matrix<double>로 변환
                var matrixSize = matrix.GetLength(0);
                var denseMatrix = Matrix<double>.Build.DenseOfArray(matrix);

                // Eigenvalue 계산
                var evd = denseMatrix.Evd();

                // 결과를 Complex32 배열로 변환
                return evd.EigenValues
                         .Select(c => new Complex32((float)c.Real, (float)c.Imaginary))
                         .ToArray();
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Eigenvalue calculation failed: {ex.Message}");
                return Array.Empty<Complex32>();
            }
        }

        private double CalculateConditionNumber(double[,] matrix)
        {
            try
            {
                var denseMatrix = Matrix<double>.Build.DenseOfArray(matrix);
                var svd = denseMatrix.Svd();

                // 조건수 = 최대 특이값 / 최소 특이값
                return svd.ConditionNumber;
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Condition number calculation failed: {ex.Message}");
                return double.NaN;
            }
        }

        private T[] RentArray<T>(int length)
        {
            return ArrayPool<T>.Shared.Rent(length);
        }

        private void ReturnArray<T>(T[] array)
        {
            if (array != null)
            {
                ArrayPool<T>.Shared.Return(array);
            }
        }

        private void ReportProgress(string stage, double percentage, string message = null)
        {
            _progress?.Report(new AnalysisProgress
            {
                Stage = stage,
                Percentage = percentage,
                Message = message ?? stage
            });

            _loggingService.Debug($"{stage}: {percentage:F1}% - {message}");
        }
    }
}
