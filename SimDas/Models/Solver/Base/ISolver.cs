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
        bool IsSteady { get; }
        int Intervals { get; set; }
        double StartTime { get; set; }
        double EndTime { get; set; }
        double[] InitialState { get; set; }

        event EventHandler<ProgressEventArgs> OnProgressChanged;

        Task<Solution> SolveAsync(CancellationToken cancellationToken = default);
        void Initialize(Dictionary<string, double> parameters);
        void Cleanup();
        void SetDifferentialEquation(DifferentialEquation equation, int dimention);
        void SetDAESystem(DAESystem daeSystem, int dimention);

    }
}
