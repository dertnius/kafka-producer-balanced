using System;
using System.Collections.Generic;
using System.Linq;

namespace MyDotNetApp.Optimization
{
    /// <summary>
    /// Meet-in-the-middle solver splitting items into two halves and combining optimal subsets.
    /// Complexity: O(2^(n/2) * log(2^(n/2))).
    /// </summary>
    public class MeetInTheMiddleKnapsackSolver
    {
        public KnapsackResult Solve(List<MortgageAsset> assets, decimal capacityDecimal)
        {
            if (assets == null || assets.Count == 0)
            {
                return new KnapsackResult { CapitalAvailable = capacityDecimal, MaximumValue = 0 };
            }

            int capacity = (int)(capacityDecimal * 100);
            int n = assets.Count;
            int mid = n / 2;

            var left = assets.Take(mid).ToList();
            var right = assets.Skip(mid).ToList();

            var leftSubsets = GenerateSubsets(left);
            var rightSubsets = GenerateSubsets(right);

            // Remove dominated subsets on the right (keep best value for each weight)
            rightSubsets.Sort((a, b) => a.weight.CompareTo(b.weight));
            var filteredRight = new List<(int weight, int value, List<MortgageAsset> items)>();
            int maxValue = -1;
            foreach (var subset in rightSubsets)
            {
                if (subset.value > maxValue)
                {
                    filteredRight.Add(subset);
                    maxValue = subset.value;
                }
            }

            int bestValue = 0;
            List<MortgageAsset> bestSet = new();

            foreach (var leftSubset in leftSubsets)
            {
                if (leftSubset.weight > capacity) continue;
                int remaining = capacity - leftSubset.weight;

                // Binary search in filteredRight for max weight <= remaining
                int idx = UpperBound(filteredRight, remaining) - 1;
                if (idx >= 0)
                {
                    int totalValue = leftSubset.value + filteredRight[idx].value;
                    if (totalValue > bestValue)
                    {
                        bestValue = totalValue;
                        bestSet = new List<MortgageAsset>();
                        bestSet.AddRange(leftSubset.items);
                        bestSet.AddRange(filteredRight[idx].items);
                    }
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

        private static List<(int weight, int value, List<MortgageAsset> items)> GenerateSubsets(List<MortgageAsset> list)
        {
            var subsets = new List<(int weight, int value, List<MortgageAsset> items)>();
            int m = list.Count;
            int total = 1 << m;
            for (int mask = 0; mask < total; mask++)
            {
                int weight = 0;
                int value = 0;
                var items = new List<MortgageAsset>();
                for (int i = 0; i < m; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        weight += (int)(list[i].CapitalRequired * 100);
                        value += (int)(list[i].ExpectedReturn * 100);
                        items.Add(list[i]);
                    }
                }
                subsets.Add((weight, value, items));
            }
            return subsets;
        }

        private static int UpperBound(List<(int weight, int value, List<MortgageAsset> items)> list, int targetWeight)
        {
            int low = 0;
            int high = list.Count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (list[mid].weight <= targetWeight)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }
            return low;
        }
    }
}
