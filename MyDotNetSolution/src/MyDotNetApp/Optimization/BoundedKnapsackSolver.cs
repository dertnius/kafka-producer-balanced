using System;
using System.Collections.Generic;
using System.Linq;

namespace MyDotNetApp.Optimization
{
    /// <summary>
    /// Bounded knapsack solver - each asset can be selected 0 to N times
    /// Useful for pool sizes where you can purchase multiple units of same security
    /// </summary>
    public class BoundedKnapsackSolver
    {
        /// <summary>
        /// Solves bounded knapsack where each asset has a maximum quantity
        /// </summary>
        /// <param name="assets">List of mortgage assets with quantities</param>
        /// <param name="quantities">Max quantity for each asset (asset ID -> max quantity)</param>
        /// <param name="capacityDecimal">Total capital available</param>
        public KnapsackResult Solve(List<MortgageAsset> assets, Dictionary<int, int> quantities, decimal capacityDecimal)
        {
            int capacity = (int)(capacityDecimal * 100);
            
            // Convert bounded knapsack to 0/1 knapsack by creating copies
            var expandedAssets = new List<MortgageAsset>();
            var assetMapping = new Dictionary<MortgageAsset, int>(); // Track original asset for each copy

            foreach (var asset in assets)
            {
                int maxQty = quantities.ContainsKey(asset.Id) ? quantities[asset.Id] : 1;
                
                for (int i = 0; i < maxQty; i++)
                {
                    var copy = new MortgageAsset
                    {
                        Id = asset.Id,
                        Name = $"{asset.Name} (Unit {i + 1})",
                        CapitalRequired = asset.CapitalRequired,
                        ExpectedReturn = asset.ExpectedReturn,
                        InterestRate = asset.InterestRate,
                        TermMonths = asset.TermMonths,
                        LoanToValueRatio = asset.LoanToValueRatio,
                        SecurityType = asset.SecurityType
                    };
                    expandedAssets.Add(copy);
                    assetMapping[copy] = asset.Id;
                }
            }

            // Use 0/1 knapsack on expanded list
            var solver = new ZeroOneKnapsackSolver();
            var result = solver.Solve(expandedAssets, capacityDecimal);

            // Consolidate results
            var consolidatedAssets = new List<MortgageAsset>();
            var consolidatedMap = new Dictionary<int, int>();

            foreach (var asset in result.SelectedAssets)
            {
                int assetId = asset.Id;
                if (!consolidatedMap.ContainsKey(assetId))
                {
                    consolidatedMap[assetId] = 0;
                    consolidatedAssets.Add(assets.First(a => a.Id == assetId));
                }
                consolidatedMap[assetId]++;
            }

            result.SelectedAssets = consolidatedAssets;
            result.AssetSelectionMap = consolidatedMap;
            result.CalculateDerivedMetrics();

            return result;
        }
    }
}
