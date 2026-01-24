using System;
using System.Collections.Generic;
using System.Linq;

namespace MyDotNetApp.Optimization
{
    /// <summary>
    /// Branch and bound solver using value-to-weight ratio upper bounds.
    /// </summary>
    public class BranchAndBoundKnapsackSolver
    {
        private record Item(int Index, int Weight, int Value, MortgageAsset Asset, decimal Ratio);

        public KnapsackResult Solve(List<MortgageAsset> assets, decimal capacityDecimal)
        {
            if (assets == null || assets.Count == 0)
            {
                return new KnapsackResult { CapitalAvailable = capacityDecimal, MaximumValue = 0 };
            }

            int capacity = (int)(capacityDecimal * 100);
            var items = assets
                .Select((a, idx) => new Item(idx, (int)(a.CapitalRequired * 100), (int)(a.ExpectedReturn * 100), a, a.CapitalRequired > 0 ? a.ExpectedReturn / a.CapitalRequired : 0))
                .OrderByDescending(i => i.Ratio)
                .ToList();

            int bestValue = 0;
            List<MortgageAsset> bestSet = new();

            void Dfs(int level, int currentWeight, int currentValue, List<MortgageAsset> chosen)
            {
                if (currentWeight > capacity) return;

                if (currentValue > bestValue)
                {
                    bestValue = currentValue;
                    bestSet = new List<MortgageAsset>(chosen);
                }

                if (level == items.Count) return;

                // Upper bound estimation using fractional item
                int bound = currentValue;
                int totalWeight = currentWeight;
                int i = level;
                while (i < items.Count && totalWeight + items[i].Weight <= capacity)
                {
                    totalWeight += items[i].Weight;
                    bound += items[i].Value;
                    i++;
                }
                if (i < items.Count)
                {
                    int remaining = capacity - totalWeight;
                    bound += (int)(items[i].Ratio * remaining);
                }

                if (bound <= bestValue) return; // prune

                // Choose item
                chosen.Add(items[level].Asset);
                Dfs(level + 1, currentWeight + items[level].Weight, currentValue + items[level].Value, chosen);
                chosen.RemoveAt(chosen.Count - 1);

                // Skip item
                Dfs(level + 1, currentWeight, currentValue, chosen);
            }

            Dfs(0, 0, 0, new List<MortgageAsset>());

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
