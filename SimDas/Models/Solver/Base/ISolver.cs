using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SimDas.Models.Common;

namespace SimDas.Models.Solver.Base
{
    public interface ISolver
    {
        string Name { get; }
        int Intervals { get; set; }
        double StartTime { get; set; }
        double EndTime { get; set; }
        double[] InitialState { get; set; }
        bool IsPaused { get; }
        Solution CurrentSolution { get; }  // 현재까지의 계산 결과

        event EventHandler<ProgressEventArgs> OnProgressChanged;

        Task<Solution> SolveAsync(CancellationToken cancellationToken = default);
        void Pause();
        void Resume();
        void Initialize(Dictionary<string, double> parameters);
        void Cleanup();
        void SetODESystem(ODESystem equation, int dimention);
        void SetDAESystem(DAESystem daeSystem, int dimention);

    }
}
