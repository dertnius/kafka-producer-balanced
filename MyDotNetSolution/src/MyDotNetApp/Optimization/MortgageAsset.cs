using System;

namespace MyDotNetApp.Optimization
{
    /// <summary>
    /// Represents a mortgage asset that can be included in a pool allocation
    /// </summary>
    public class MortgageAsset
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal CapitalRequired { get; set; } // Weight in knapsack
        public decimal ExpectedReturn { get; set; }  // Value in knapsack
        public decimal InterestRate { get; set; }
        public int TermMonths { get; set; }
        public decimal LoanToValueRatio { get; set; }
        public string? SecurityType { get; set; } // Residential, Commercial, etc.
        
        /// <summary>
        /// Calculate return on investment ratio (Return / Capital)
        /// </summary>
        public decimal GetROIRatio() => CapitalRequired > 0 
            ? ExpectedReturn / CapitalRequired 
            : 0;

        /// <summary>
        /// Calculate annual return based on interest rate
        /// </summary>
        public decimal GetAnnualReturn() => CapitalRequired * InterestRate;

        public override string ToString() => 
            $"Asset {Id}: {Name} - Capital: ${CapitalRequired}, Return: ${ExpectedReturn}, ROI: {GetROIRatio():P2}";
    }
}
