using System;
using System.Collections.Generic;
using System.Linq;

namespace MyDotNetApp.Optimization
{
    /// <summary>
    /// Fully Polynomial-Time Approximation Scheme (FPTAS) for 0/1 knapsack.
    /// </summary>
    public class ApproximateKnapsackSolver
    {
        /// <summary>
        /// Solve with approximation factor (1 - epsilon).
        /// </summary>
        /// <param name="assets">List of items</param>
        /// <param name="capacityDecimal">Capacity</param>
        /// <param name="epsilon">Approximation parameter (0 < epsilon < 1)</param>
        public KnapsackResult Solve(List<MortgageAsset> assets, decimal capacityDecimal, double epsilon = 0.1)
        {
            if (assets == null || assets.Count == 0)
            {
                return new KnapsackResult { CapitalAvailable = capacityDecimal, MaximumValue = 0 };
            }

            int n = assets.Count;
            int capacity = (int)(capacityDecimal * 100);

            int maxValue = assets.Max(a => (int)(a.ExpectedReturn * 100));
            if (maxValue == 0) return new KnapsackResult { CapitalAvailable = capacityDecimal, MaximumValue = 0 };

            decimal eps = (decimal)epsilon;
            decimal scale = eps * maxValue / n;
            if (scale < 1) scale = 1;

            int[] scaledValues = assets.Select(a => (int)Math.Floor((a.ExpectedReturn * 100) / scale)).ToArray();
            int sumScaledValues = scaledValues.Sum();

            // DP on value (min weight for value)
            int INF = int.MaxValue / 4;
            int[] dp = Enumerable.Repeat(INF, sumScaledValues + 1).ToArray();
            dp[0] = 0;

            for (int i = 0; i < n; i++)
            {
                int weight = (int)(assets[i].CapitalRequired * 100);
                int value = scaledValues[i];
                for (int v = sumScaledValues; v >= value; v--)
                {
                    if (dp[v - value] + weight < dp[v])
                    {
                        dp[v] = dp[v - value] + weight;
                    }
                }
            }

            int bestScaled = 0;
            for (int v = 0; v <= sumScaledValues; v++)
            {
                if (dp[v] <= capacity) bestScaled = v;
            }

            // Reconstruct approximate solution
            var selected = new List<MortgageAsset>();
            int remainingValue = bestScaled;
            for (int i = n - 1; i >= 0 && remainingValue > 0; i--)
            {
                int weight = (int)(assets[i].CapitalRequired * 100);
                int value = scaledValues[i];
                if (remainingValue >= value && dp[remainingValue - value] + weight == dp[remainingValue])
                {
                    selected.Add(assets[i]);
                    remainingValue -= value;
                }
            }

            var result = new KnapsackResult
            {
                MaximumValue = selected.Sum(a => a.ExpectedReturn),
                CapitalAvailable = capacityDecimal,
                SelectedAssets = selected
            };
            foreach (var asset in selected)
            {
                result.AssetSelectionMap[asset.Id] = 1;
            }
            result.CalculateDerivedMetrics();
            return result;
        }
    }
}
