using System;
using System.Collections.Generic;
using MathNet.Numerics;

namespace SimDas.Models.Analysis
{
    public class DAEAnalysis
    {
        public int Index { get; set; }
        public bool[] AlgebraicVariables { get; set; }
        public string[] Warnings { get; set; }
        public bool IsStiff { get; set; }
        public double ConditionNumber { get; set; }
        public double StiffnessRatio { get; set; }
        public Complex32[] Eigenvalues { get; set; }

        public DAEAnalysis()
        {
            AlgebraicVariables = Array.Empty<bool>();
            Warnings = Array.Empty<string>();
            Eigenvalues = Array.Empty<Complex32>();
        }
    }
}
