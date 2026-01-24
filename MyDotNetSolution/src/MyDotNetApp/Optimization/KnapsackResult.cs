using System;
using System.Collections.Generic;
using System.Linq;

namespace MyDotNetApp.Optimization
{
    /// <summary>
    /// Result of a knapsack optimization for mortgage capital allocation
    /// </summary>
    public class KnapsackResult
    {
        public decimal MaximumValue { get; set; }
        public decimal TotalCapitalUsed { get; set; }
        public decimal CapitalAvailable { get; set; }
        public decimal CapitalUnused { get; set; }
        public List<MortgageAsset> SelectedAssets { get; set; } = new();
        public Dictionary<int, int> AssetSelectionMap { get; set; } = new(); // Asset ID -> Quantity selected
        public decimal CapitalUtilizationRate { get; set; }
        public decimal WeightedAverageReturn { get; set; }

        public void CalculateDerivedMetrics()
        {
            TotalCapitalUsed = SelectedAssets.Sum(a => a.CapitalRequired);
            CapitalUnused = CapitalAvailable - TotalCapitalUsed;
            CapitalUtilizationRate = CapitalAvailable > 0 
                ? TotalCapitalUsed / CapitalAvailable 
                : 0;
            
            if (TotalCapitalUsed > 0)
            {
                WeightedAverageReturn = SelectedAssets.Sum(a => a.ExpectedReturn) / TotalCapitalUsed;
            }
        }

        public override string ToString()
        {
            return $@"Knapsack Optimization Result:
  Maximum Return: ${MaximumValue:F2}
  Total Capital Used: ${TotalCapitalUsed:F2}
  Capital Available: ${CapitalAvailable:F2}
  Capital Unused: ${CapitalUnused:F2}
  Utilization Rate: {CapitalUtilizationRate:P2}
  Weighted Avg Return: {WeightedAverageReturn:P4}
  Assets Selected: {SelectedAssets.Count}";
        }
    }
}
