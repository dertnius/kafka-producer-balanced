using System;
using System.Collections.Generic;
using System.Linq;
using MyDotNetApp.Optimization;

namespace MyDotNetApp.Services.Optimization
{
    /// <summary>
    /// Service for optimizing mortgage capital pool allocation
    /// </summary>
    public interface IMortgagePoolOptimizer
    {
        /// <summary>
        /// Optimize pool using 0/1 knapsack (select each asset 0 or 1 times)
        /// </summary>
        KnapsackResult OptimizeZeroOne(List<MortgageAsset> assets, decimal poolSize);

        /// <summary>
        /// Optimize pool using bounded knapsack (select each asset up to N times)
        /// </summary>
        KnapsackResult OptimizeBounded(List<MortgageAsset> assets, Dictionary<int, int> quantities, decimal poolSize);

        /// <summary>
        /// Optimize pool using unbounded knapsack (select any asset unlimited times)
        /// </summary>
        KnapsackResult OptimizeUnbounded(List<MortgageAsset> assets, decimal poolSize);

        /// <summary>
        /// Optimize using greedy approach (fast approximation)
        /// </summary>
        KnapsackResult OptimizeGreedy(List<MortgageAsset> assets, decimal poolSize);

        /// <summary>
        /// Filter assets by security type
        /// </summary>
        List<MortgageAsset> FilterBySecurityType(List<MortgageAsset> assets, string securityType);

        /// <summary>
        /// Filter assets by LTV range
        /// </summary>
        List<MortgageAsset> FilterByLTV(List<MortgageAsset> assets, decimal minLTV, decimal maxLTV);

        /// <summary>
        /// Filter assets by interest rate range
        /// </summary>
        List<MortgageAsset> FilterByRate(List<MortgageAsset> assets, decimal minRate, decimal maxRate);
    }

    public class MortgagePoolOptimizer : IMortgagePoolOptimizer
    {
        private readonly ZeroOneKnapsackSolver _zeroOneSolver = new();
        private readonly BoundedKnapsackSolver _boundedSolver = new();
        private readonly UnboundedKnapsackSolver _unboundedSolver = new();

        public KnapsackResult OptimizeZeroOne(List<MortgageAsset> assets, decimal poolSize)
        {
            return _zeroOneSolver.Solve(assets, poolSize);
        }

        public KnapsackResult OptimizeBounded(List<MortgageAsset> assets, Dictionary<int, int> quantities, decimal poolSize)
        {
            return _boundedSolver.Solve(assets, quantities, poolSize);
        }

        public KnapsackResult OptimizeUnbounded(List<MortgageAsset> assets, decimal poolSize)
        {
            return _unboundedSolver.Solve(assets, poolSize);
        }

        public KnapsackResult OptimizeGreedy(List<MortgageAsset> assets, decimal poolSize)
        {
            return _zeroOneSolver.SolveGreedy(assets, poolSize);
        }

        public List<MortgageAsset> FilterBySecurityType(List<MortgageAsset> assets, string securityType)
        {
            return assets.Where(a => a.SecurityType == securityType).ToList();
        }

        public List<MortgageAsset> FilterByLTV(List<MortgageAsset> assets, decimal minLTV, decimal maxLTV)
        {
            return assets.Where(a => a.LoanToValueRatio >= minLTV && a.LoanToValueRatio <= maxLTV).ToList();
        }

        public List<MortgageAsset> FilterByRate(List<MortgageAsset> assets, decimal minRate, decimal maxRate)
        {
            return assets.Where(a => a.InterestRate >= minRate && a.InterestRate <= maxRate).ToList();
        }
    }
}
