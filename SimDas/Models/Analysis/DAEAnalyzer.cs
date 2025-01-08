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
        private readonly ArrayPool<double> _arrayPool = ArrayPool<double>.Shared;

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
                StiffnessRatio = double.NaN,
                VariableDependencies = new Dictionary<int, HashSet<int>>(),
                CircularDependencyPaths = new List<string>()
            };

            try
            {
                // 의존성 분석
                AnalyzeDependencies(system, initialState, dimension, analysis);

                // 대수 변수 식별
                IdentifyAlgebraicVariables(system, initialState, initialTime, analysis);

                // Index 분석
                analysis.Index = DetermineIndex(system, initialState, initialTime, analysis.AlgebraicVariables, analysis);

                // Jacobian 계산
                var jacobian = CalculateJacobian(system, initialState, new double[dimension], initialTime);

                // 조건수 계산
                analysis.ConditionNumber = CalculateConditionNumber(jacobian);

                // Stiffness 분석 및 비율 계산
                var eigenvalues = CalculateEigenvalues(jacobian);
                analysis.IsStiff = CheckStiffness(system, initialState, initialTime);
                analysis.Eigenvalues = eigenvalues;

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
                Eigenvalues = Array.Empty<Complex32>(),
                VariableDependencies = new Dictionary<int, HashSet<int>>(),
                CircularDependencyPaths = new List<string>()
            };

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 변수 의존성 분석
                await AnalyzeDependenciesAsync(system, initialState, dimension, analysis);

                // 대수 변수 식별
                await IdentifyAlgebraicVariablesAsync(system, initialState, initialTime, analysis);

                cancellationToken.ThrowIfCancellationRequested();
                // Index 분석
                analysis.Index = await DetermineIndexAsync(system, initialState, initialTime, analysis.AlgebraicVariables, analysis);


                cancellationToken.ThrowIfCancellationRequested();
                // Jacobian 계산
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

        private void AnalyzeDependencies(
            DAESystem system,
            double[] state,
            int dimension,
            DAEAnalysis analysis)
        {
            var dependencies = new Dictionary<int, HashSet<int>>();
            var visitedNodes = new HashSet<int>();
            var currentPath = new HashSet<int>();

            // 각 변수에 대한 의존성 분석
            for (int i = 0; i < dimension; i++)
            {
                if (!dependencies.ContainsKey(i))
                {
                    dependencies[i] = new HashSet<int>();
                    DetectDependencies(i, dependencies, visitedNodes, currentPath, system, state, dimension, analysis);
                }
            }

            analysis.VariableDependencies = dependencies;
        }

        private async Task AnalyzeDependenciesAsync(
            DAESystem system,
            double[] state,
            int dimension,
            DAEAnalysis analysis)
        {
            await Task.Run(() =>
            {
                AnalyzeDependencies(system, state, dimension, analysis);
            });
        }

        private void DetectDependencies(
            int currentVar,
            Dictionary<int, HashSet<int>> dependencies,
            HashSet<int> visitedNodes,
            HashSet<int> currentPath,
            DAESystem system,
            double[] state,
            int dimension,
            DAEAnalysis analysis)
        {
            if (currentPath.Contains(currentVar))
            {
                // 순환 의존성 발견
                var cycle = new List<int>(currentPath) { currentVar };
                var cyclePath = string.Join(" → ", cycle.Select(v => $"var_{v}"));
                if (!string.IsNullOrEmpty(cyclePath))
                {
                    analysis.HasCircularDependency = true;
                    analysis.CircularDependencyPaths.Add(cyclePath);
                }
                return;
            }

            if (visitedNodes.Contains(currentVar))
            {
                return;
            }

            visitedNodes.Add(currentVar);
            currentPath.Add(currentVar);

            // 변수 의존성 검사
            double[] derivatives = new double[dimension];
            var baseState = (double[])state.Clone();
            var baseResiduals = system(0, baseState, derivatives);

            for (int i = 0; i < dimension; i++)
            {
                if (i == currentVar) continue;

                baseState[i] += PERTURBATION;
                var perturbedResiduals = system(0, baseState, derivatives);
                baseState[i] -= PERTURBATION;

                // 의존성 검사
                for (int j = 0; j < dimension; j++)
                {
                    if (Math.Abs(perturbedResiduals[j] - baseResiduals[j]) > PERTURBATION)
                    {
                        dependencies[currentVar].Add(i);
                        if (!dependencies.ContainsKey(i))
                        {
                            dependencies[i] = new HashSet<int>();
                            DetectDependencies(i, dependencies, visitedNodes, currentPath, system, state, dimension, analysis);
                        }
                    }
                }
            }

            currentPath.Remove(currentVar);
        }

        private void IdentifyAlgebraicVariables(DAESystem system, double[] state, double time, DAEAnalysis analysis)
        {
            // 초기화 - 기본값을 false로 변경 (미분 변수로 가정)
            for (int i = 0; i < state.Length; i++)
            {
                analysis.AlgebraicVariables[i] = false;
            }

            double[] derivatives = new double[state.Length];
            double[] baseResiduals = system(time, state, derivatives);

            Parallel.For(0, state.Length, i =>
            {
                var testDerivatives = (double[])derivatives.Clone();
                testDerivatives[i] += PERTURBATION;

                var perturbedResiduals = system(time, state, testDerivatives);

                // 민감도 분석을 통한 대수 변수 식별
                double sensitivity = 0.0;
                for (int j = 0; j < state.Length; j++)
                {
                    sensitivity += Math.Abs(perturbedResiduals[j] - baseResiduals[j]);
                }

                // 임계값보다 작은 민감도를 가진 경우 대수 변수로 판단
                if (sensitivity < PERTURBATION)
                {
                    analysis.AlgebraicVariables[i] = true;
                }
            });
        }

        private async Task IdentifyAlgebraicVariablesAsync(
            DAESystem system,
            double[] state,
            double time,
            DAEAnalysis analysis)
        {
            await Task.Run(() =>
            {
                IdentifyAlgebraicVariables(system, state, time, analysis);
            });
        }

        private int DetermineIndex(DAESystem system, double[] state, double time, bool[] algebraicVars, DAEAnalysis analysis)
        {
            int index = 1;
            double[] derivatives = new double[state.Length];
            bool[] currentAlgebraic = (bool[])algebraicVars.Clone();

            while (currentAlgebraic.Any(x => x) && index < 4)
            {
                bool[] nextAlgebraic = new bool[state.Length];
                double[] baseResiduals = system(time, state, derivatives);

                bool foundDependency = false;
                for (int i = 0; i < state.Length; i++)
                {
                    if (!currentAlgebraic[i]) continue;

                    nextAlgebraic[i] = true;
                    foreach (var dep in analysis.VariableDependencies[i])
                    {
                        if (!currentAlgebraic[dep])
                        {
                            nextAlgebraic[i] = false;
                            foundDependency = true;
                            break;
                        }
                    }
                }

                if (!foundDependency) break;
                currentAlgebraic = nextAlgebraic;
                index++;
            }

            return index;
        }

        private async Task<int> DetermineIndexAsync(
            DAESystem system,
            double[] state,
            double time,
            bool[] algebraicVars,
            DAEAnalysis analysis)  // analysis 매개변수 추가
        {
            return await Task.Run(() =>
            {
                return DetermineIndex(system, state, time, algebraicVars, analysis);  // analysis 전달
            });
        }

        private bool CheckStiffness(DAESystem system, double[] state, double time)
        {
            try
            {
                double[] derivatives = new double[state.Length];
                var jacobian = CalculateJacobian(system, state, derivatives, time);
                var eigenvalues = CalculateEigenvalues(jacobian);

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
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Stiffness check failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CheckStiffnessAsync(DAESystem system, double[] state, double time)
        {
            return await Task.Run(() => CheckStiffness(system, state, time));
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

            if (analysis.HasCircularDependency)
            {
                warnings.Add("Circular dependencies detected in the system:");
                foreach (var path in analysis.CircularDependencyPaths)
                {
                    warnings.Add($"  Circular path: {path}");
                }
            }

            var stronglyConnected = FindStronglyConnectedComponents(analysis.VariableDependencies);
            if (stronglyConnected.Any(component => component.Count > 1))
            {
                warnings.Add("Strongly connected components detected:");
                foreach (var component in stronglyConnected.Where(c => c.Count > 1))
                {
                    warnings.Add($"  Variables: {string.Join(", ", component.Select(v => $"var_{v}"))}");
                }
            }

            analysis.Warnings = warnings.ToArray();
        }

        private List<HashSet<int>> FindStronglyConnectedComponents(
            Dictionary<int, HashSet<int>> dependencies)
        {
            var components = new List<HashSet<int>>();
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            var onStack = new HashSet<int>();
            var indices = new Dictionary<int, int>();
            var lowLinks = new Dictionary<int, int>();
            int index = 0;

            foreach (var vertex in dependencies.Keys)
            {
                if (!visited.Contains(vertex))
                {
                    StrongConnect(vertex);
                }
            }

            void StrongConnect(int v)
            {
                indices[v] = index;
                lowLinks[v] = index;
                index++;
                stack.Push(v);
                onStack.Add(v);
                visited.Add(v);

                foreach (var w in dependencies[v])
                {
                    if (!indices.ContainsKey(w))
                    {
                        StrongConnect(w);
                        lowLinks[v] = Math.Min(lowLinks[v], lowLinks[w]);
                    }
                    else if (onStack.Contains(w))
                    {
                        lowLinks[v] = Math.Min(lowLinks[v], indices[w]);
                    }
                }

                if (lowLinks[v] == indices[v])
                {
                    var component = new HashSet<int>();
                    int w;
                    do
                    {
                        w = stack.Pop();
                        onStack.Remove(w);
                        component.Add(w);
                    } while (w != v);
                    components.Add(component);
                }
            }

            return components;
        }

        private double[,] CalculateJacobian(DAESystem system, double[] state, double[] derivatives, double time)
        {
            int n = state.Length;
            var jacobian = new double[n, n];
            double[] baseResiduals = system(time, state, derivatives);

            // 적응형 섭동 크기 사용
            double eps = Math.Sqrt(PERTURBATION);
            double baseNorm = CalculateNorm(state);
            if (baseNorm > 0)
            {
                eps *= Math.Max(baseNorm, 1.0);
            }

            for (int i = 0; i < n; i++)
            {
                var perturbedState = (double[])state.Clone();
                perturbedState[i] += eps;
                double[] perturbedResiduals = system(time, perturbedState, derivatives);

                for (int j = 0; j < n; j++)
                {
                    jacobian[j, i] = (perturbedResiduals[j] - baseResiduals[j]) / eps;
                }
            }

            return jacobian;
        }

        private double CalculateNorm(double[] vector)
        {
            return Math.Sqrt(vector.Sum(x => x * x));
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
            double eps = Math.Sqrt(PERTURBATION);
            double baseNorm = CalculateNorm(state);
            if (baseNorm > 0)
            {
                eps *= Math.Max(baseNorm, 1.0);
            }

            const int MIN_BATCH_SIZE = 4;
            int batchSize = Math.Max(MIN_BATCH_SIZE, n / Environment.ProcessorCount);
            int numBatches = (n + batchSize - 1) / batchSize;

            // 클래스 레벨의 ArrayPool 사용
            double[] baseResiduals = _arrayPool.Rent(n);

            try
            {
                Array.Copy(system(time, state, derivatives), baseResiduals, n);
                ReportProgress("Calculating Jacobian", 0, "Starting Jacobian calculation...");

                var tasks = new Task[numBatches];
                for (int batch = 0; batch < numBatches; batch++)
                {
                    int batchStart = batch * batchSize;
                    int batchEnd = Math.Min(batchStart + batchSize, n);
                    int batchIndex = batch;

                    tasks[batch] = Task.Run(() =>
                    {
                        // 각 태스크별 배열 할당
                        double[] perturbedState = _arrayPool.Rent(n);

                        try
                        {
                            for (int col = batchStart; col < batchEnd && !cancellationToken.IsCancellationRequested; col++)
                            {
                                Array.Copy(state, perturbedState, n);
                                perturbedState[col] += eps;

                                var perturbedResiduals = system(time, perturbedState, derivatives);

                                for (int row = 0; row < n; row++)
                                {
                                    jacobian[row, col] = (perturbedResiduals[row] - baseResiduals[row]) / eps;
                                }

                                double progress = (double)(col - batchStart) / (batchEnd - batchStart) * 100;
                                ReportProgress("Calculating Jacobian", progress,
                                    $"Batch {batchIndex + 1}/{numBatches}: {progress:F1}%");
                            }
                        }
                        finally
                        {
                            // 배열 반환
                            _arrayPool.Return(perturbedState);
                        }
                    }, cancellationToken);
                }

                await Task.WhenAll(tasks);
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
                // baseResiduals 반환
                _arrayPool.Return(baseResiduals);
            }

            return jacobian;
        }

        private Complex32[] CalculateEigenvalues(double[,] matrix)
        {
            try
            {
                var matrixSize = matrix.GetLength(0);
                var denseMatrix = Matrix<double>.Build.DenseOfArray(matrix);

                // 행렬이 특이한지(singular) 확인
                if (Math.Abs(denseMatrix.Determinant()) < 1e-10)
                {
                    _loggingService.Warning("Matrix is near-singular, eigenvalues may be unreliable");
                }

                var evd = denseMatrix.Evd();
                var eigenvalues = evd.EigenValues
                    .Select(c => new Complex32((float)c.Real, (float)c.Imaginary))
                    .Where(c => !float.IsNaN(c.Real) && !float.IsInfinity(c.Real))
                    .ToArray();

                // 결과 로깅
                _loggingService.Debug($"Calculated {eigenvalues.Length} eigenvalues");
                return eigenvalues;
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

                var maxSingular = svd.S[0];
                var minSingular = svd.S[svd.S.Count - 1];

                if (Math.Abs(minSingular) < 1e-10)
                {
                    _loggingService.Warning("Matrix is near-singular");
                    return double.PositiveInfinity;
                }

                var conditionNumber = maxSingular / minSingular;
                _loggingService.Debug($"Calculated condition number: {conditionNumber}");
                return conditionNumber;
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Condition number calculation failed: {ex.Message}");
                return double.NaN;
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
