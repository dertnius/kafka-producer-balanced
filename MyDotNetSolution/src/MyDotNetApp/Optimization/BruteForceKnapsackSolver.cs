using System;
using System.Collections.Generic;
using System.Linq;

namespace MyDotNetApp.Optimization
{
    /// <summary>
    /// Brute force solver enumerating all subsets (use only for very small item counts).
    /// Complexity: O(2^n).
    /// </summary>
    public class BruteForceKnapsackSolver
    {
        public KnapsackResult Solve(List<MortgageAsset> assets, decimal capacityDecimal)
        {
            if (assets == null || assets.Count == 0)
            {
                return new KnapsackResult { CapitalAvailable = capacityDecimal, MaximumValue = 0 };
            }

            int n = assets.Count;
            if (n > 25)
            {
                throw new InvalidOperationException("Brute force solver is limited to 25 assets or fewer.");
            }

            int capacity = (int)(capacityDecimal * 100);
            int bestValue = 0;
            List<MortgageAsset> bestSet = new();

            int totalMasks = 1 << n;
            for (int mask = 0; mask < totalMasks; mask++)
            {
                int weight = 0;
                int value = 0;
                List<MortgageAsset> current = new();

                for (int i = 0; i < n; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        weight += (int)(assets[i].CapitalRequired * 100);
                        value += (int)(assets[i].ExpectedReturn * 100);
                        current.Add(assets[i]);
                    }
                }

                if (weight <= capacity && value > bestValue)
                {
                    bestValue = value;
                    bestSet = current;
                }
            }

            var result = new KnapsackResult
            {
                MaximumValue = bestValue / 100m,
                CapitalAvailable = capacityDecimal,
                SelectedAssets = bestSet
            };

            foreach (var asset in bestSet)
            {
                result.AssetSelectionMap[asset.Id] = 1;
            }

            result.CalculateDerivedMetrics();
            return result;
        }
    }
}
