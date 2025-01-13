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
        public Dictionary<int, HashSet<int>> VariableDependencies { get; set; }
        public bool HasCircularDependency { get; set; }
        public List<string> CircularDependencyPaths { get; set; }
        public SystemStructure SystemStructure { get; set; }

        public DAEAnalysis()
        {
            AlgebraicVariables = Array.Empty<bool>();
            Warnings = Array.Empty<string>();
            Eigenvalues = Array.Empty<Complex32>();
            SystemStructure = new SystemStructure();
        }
    }

    public class SystemStructure
    {
        public List<HashSet<int>> Blocks { get; set; } = new();
        public List<HashSet<int>> AlgebraicBlocks { get; set; } = new();
        public List<HashSet<int>> DifferentialBlocks { get; set; } = new();
        public List<HashSet<int>> MixedBlocks { get; set; } = new();
        public HashSet<int> SingleEquations { get; set; } = new();
        public HashSet<int> AlgebraicEquations { get; set; } = new();
        public HashSet<int> DifferentialEquations { get; set; } = new();
        public List<BlockAnalysis> BlockAnalyses { get; set; } = new();
        public bool IsFullyCoupled { get; set; }
        public bool HasAlgebraicLoop { get; set; }
    }

    public class BlockAnalysis
    {
        public List<int> Variables { get; set; }
        public double ConditionNumber { get; set; }
        public Complex32[] Eigenvalues { get; set; }
    }

    public class BlockInfo
    {
        public int BlockIndex { get; set; }
        public string BlockType { get; set; }
        public List<string> Variables { get; set; }
        public double ConditionNumber { get; set; }
        public List<string> Eigenvalues { get; set; }
    }
}