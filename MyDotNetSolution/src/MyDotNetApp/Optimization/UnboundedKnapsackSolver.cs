using System;
using System.Collections.Generic;
using System.Linq;

namespace MyDotNetApp.Optimization
{
    /// <summary>
    /// Unbounded knapsack solver - each asset can be selected unlimited times
    /// </summary>
    public class UnboundedKnapsackSolver
    {
        /// <summary>
        /// Solves unbounded knapsack where any asset can be selected multiple times
        /// </summary>
        public KnapsackResult Solve(List<MortgageAsset> assets, decimal capacityDecimal)
        {
            if (assets == null || assets.Count == 0)
            {
                return new KnapsackResult 
                { 
                    CapitalAvailable = capacityDecimal,
                    MaximumValue = 0 
                };
            }

            int capacity = (int)(capacityDecimal * 100);
            int[] weights = assets.Select(a => (int)(a.CapitalRequired * 100)).ToArray();
            int[] values = assets.Select(a => (int)(a.ExpectedReturn * 100)).ToArray();
            int n = assets.Count;

            // DP table where dp[w] = maximum value achievable with capacity w
            int[] dp = new int[capacity + 1];

            // Build DP table
            for (int w = 0; w <= capacity; w++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (weights[i] <= w)
                    {
                        dp[w] = Math.Max(dp[w], dp[w - weights[i]] + values[i]);
                    }
                }
            }

            // Backtrack to find selected items
            var selectedAssets = new List<MortgageAsset>();
            var assetCountMap = new Dictionary<int, int>();
            int remainingCapacity = capacity;

            while (remainingCapacity > 0)
            {
                bool found = false;
                
                for (int i = 0; i < n; i++)
                {
                    if (weights[i] <= remainingCapacity && 
                        dp[remainingCapacity] == dp[remainingCapacity - weights[i]] + values[i])
                    {
                        selectedAssets.Add(assets[i]);
                        assetCountMap[assets[i].Id] = assetCountMap.ContainsKey(assets[i].Id) 
                            ? assetCountMap[assets[i].Id] + 1 
                            : 1;
                        remainingCapacity -= weights[i];
                        found = true;
                        break;
                    }
                }

                if (!found)
                    break;
            }

            var result = new KnapsackResult
            {
                MaximumValue = dp[capacity] / 100m,
                CapitalAvailable = capacityDecimal,
                SelectedAssets = selectedAssets.DistinctBy(a => a.Id).ToList(),
                AssetSelectionMap = assetCountMap
            };

            result.CalculateDerivedMetrics();
            return result;
        }
    }
}
