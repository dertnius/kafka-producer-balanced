using System;
using System.Collections.Generic;
using System.Linq;

namespace MyDotNetApp.Optimization
{
    /// <summary>
    /// 0/1 Knapsack solver for optimal mortgage capital allocation
    /// Using dynamic programming approach
    /// </summary>
    public class ZeroOneKnapsackSolver
    {
        /// <summary>
        /// Solves the 0/1 knapsack problem for mortgage capital allocation
        /// Each asset can be selected once (0 or 1 times)
        /// </summary>
        /// <param name="assets">List of mortgage assets to consider</param>
        /// <param name="capacityDecimal">Total capital available (pool size)</param>
        /// <returns>Optimization result with selected assets</returns>
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

            // Convert decimal to integer (work in cents to avoid floating point issues)
            int capacity = (int)(capacityDecimal * 100);
            int[] weights = assets.Select(a => (int)(a.CapitalRequired * 100)).ToArray();
            int[] values = assets.Select(a => (int)(a.ExpectedReturn * 100)).ToArray();
            int n = assets.Count;

            // DP table where dp[i][w] = maximum value using first i items with capacity w
            int[][] dp = new int[n + 1][];
            for (int i = 0; i <= n; i++)
            {
                dp[i] = new int[capacity + 1];
            }

            // Build the DP table
            for (int i = 1; i <= n; i++)
            {
                for (int w = 0; w <= capacity; w++)
                {
                    // Don't include item i-1
                    dp[i][w] = dp[i - 1][w];

                    // Include item i-1 if it fits
                    if (weights[i - 1] <= w)
                    {
                        int valueWithItem = dp[i - 1][w - weights[i - 1]] + values[i - 1];
                        dp[i][w] = Math.Max(dp[i][w], valueWithItem);
                    }
                }
            }

            // Backtrack to find which items were selected
            List<MortgageAsset> selectedAssets = new();
            int remainingCapacity = capacity;
            
            for (int i = n; i > 0 && remainingCapacity > 0; i--)
            {
                // If value comes from including current item
                if (dp[i][remainingCapacity] != dp[i - 1][remainingCapacity])
                {
                    selectedAssets.Add(assets[i - 1]);
                    remainingCapacity -= weights[i - 1];
                }
            }

            // Build result
            var result = new KnapsackResult
            {
                MaximumValue = dp[n][capacity] / 100m,
                CapitalAvailable = capacityDecimal,
                SelectedAssets = selectedAssets
            };

            result.SelectedAssets.ForEach(asset =>
            {
                result.AssetSelectionMap[asset.Id] = 1;
            });

            result.CalculateDerivedMetrics();
            return result;
        }

        /// <summary>
        /// Greedy approximation - useful for very large asset lists
        /// Sorts by ROI and selects greedily
        /// </summary>
        public KnapsackResult SolveGreedy(List<MortgageAsset> assets, decimal capacityDecimal)
        {
            var selectedAssets = new List<MortgageAsset>();
            decimal remainingCapacity = capacityDecimal;

            // Sort by ROI ratio descending
            var sortedByROI = assets.OrderByDescending(a => a.GetROIRatio()).ToList();

            foreach (var asset in sortedByROI)
            {
                if (asset.CapitalRequired <= remainingCapacity)
                {
                    selectedAssets.Add(asset);
                    remainingCapacity -= asset.CapitalRequired;
                }
            }

            var result = new KnapsackResult
            {
                MaximumValue = selectedAssets.Sum(a => a.ExpectedReturn),
                CapitalAvailable = capacityDecimal,
                SelectedAssets = selectedAssets
            };

            selectedAssets.ForEach(asset =>
            {
                result.AssetSelectionMap[asset.Id] = 1;
            });

            result.CalculateDerivedMetrics();
            return result;
        }
    }
}
