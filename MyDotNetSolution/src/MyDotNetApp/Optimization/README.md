# Knapsack Optimization for Mortgage Capital Pool Allocation

## Overview

This implementation provides three variants of the Knapsack algorithm optimized for allocating mortgage capital pools:

### Algorithm Variants

1. **0/1 Knapsack** - Select each asset once (optimal solution via dynamic programming)
2. **Bounded Knapsack** - Select each asset up to N times (expansion-based approach)
3. **Unbounded Knapsack** - Select any asset unlimited times (space-optimized DP)
4. **Greedy Approximation** - Fast heuristic based on ROI ranking

## Core Classes

### MortgageAsset
Represents a mortgage security in the pool.

```csharp
public class MortgageAsset
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal CapitalRequired { get; set; }      // Weight
    public decimal ExpectedReturn { get; set; }       // Value
    public decimal InterestRate { get; set; }
    public int TermMonths { get; set; }
    public decimal LoanToValueRatio { get; set; }
    public string SecurityType { get; set; }          // Residential/Commercial
    
    public decimal GetROIRatio() { ... }
    public decimal GetAnnualReturn() { ... }
}
```

### KnapsackResult
Contains optimization results and metrics.

```csharp
public class KnapsackResult
{
    public decimal MaximumValue { get; set; }
    public decimal TotalCapitalUsed { get; set; }
    public decimal CapitalAvailable { get; set; }
    public decimal CapitalUnused { get; set; }
    public List<MortgageAsset> SelectedAssets { get; set; }
    public Dictionary<int, int> AssetSelectionMap { get; set; }
    public decimal CapitalUtilizationRate { get; set; }
    public decimal WeightedAverageReturn { get; set; }
}
```

## Usage Examples

### Basic 0/1 Knapsack

```csharp
var solver = new ZeroOneKnapsackSolver();
var assets = new List<MortgageAsset>
{
    new MortgageAsset
    {
        Id = 1,
        Name = "Residential Mortgage A",
        CapitalRequired = 100_000,
        ExpectedReturn = 8_000,
        InterestRate = 0.08m,
        TermMonths = 360,
        LoanToValueRatio = 0.80m,
        SecurityType = "Residential"
    },
    new MortgageAsset
    {
        Id = 2,
        Name = "Commercial Mortgage B",
        CapitalRequired = 150_000,
        ExpectedReturn = 15_000,
        InterestRate = 0.10m,
        TermMonths = 240,
        LoanToValueRatio = 0.75m,
        SecurityType = "Commercial"
    }
};

decimal poolSize = 500_000; // $500k pool

var result = solver.Solve(assets, poolSize);

Console.WriteLine($"Maximum Return: ${result.MaximumValue:F2}");
Console.WriteLine($"Capital Used: ${result.TotalCapitalUsed:F2}");
Console.WriteLine($"Utilization: {result.CapitalUtilizationRate:P2}");
Console.WriteLine($"Selected Assets: {result.SelectedAssets.Count}");
```

### Greedy Approximation (Fast)

```csharp
var solver = new ZeroOneKnapsackSolver();
var result = solver.SolveGreedy(assets, poolSize);
// Runs in O(n log n) time instead of O(n * capacity)
```

### Bounded Knapsack (Multiple Units)

```csharp
var solver = new BoundedKnapsackSolver();
var quantities = new Dictionary<int, int>
{
    { 1, 3 },  // Asset 1 can be selected up to 3 times
    { 2, 2 }   // Asset 2 can be selected up to 2 times
};

var result = solver.Solve(assets, quantities, poolSize);
```

### Unbounded Knapsack

```csharp
var solver = new UnboundedKnapsackSolver();
var result = solver.Solve(assets, poolSize);
// Can select any asset multiple times
```

### Using MortgagePoolOptimizer Service

```csharp
var optimizer = new MortgagePoolOptimizer();

// Optimize all assets
var result = optimizer.OptimizeZeroOne(assets, 5_000_000);

// Filter by security type then optimize
var residentialOnly = optimizer.FilterBySecurityType(assets, "Residential");
var result = optimizer.OptimizeZeroOne(residentialOnly, 2_000_000);

// Filter by LTV range
var lowRiskAssets = optimizer.FilterByLTV(assets, 0.60m, 0.80m);
var result = optimizer.OptimizeZeroOne(lowRiskAssets, 3_000_000);

// Filter by interest rate
var highYieldAssets = optimizer.FilterByRate(assets, 0.10m, 0.15m);
var result = optimizer.OptimizeZeroOne(highYieldAssets, 2_000_000);
```

## Algorithm Complexity

| Algorithm | Time | Space | Notes |
|-----------|------|-------|-------|
| 0/1 DP | O(n * W) | O(n * W) | Optimal, W = capacity in cents |
| Greedy | O(n log n) | O(n) | Fast approximation |
| Bounded | O(n * Q * W) | O(n * Q * W) | Q = max quantity per asset |
| Unbounded | O(n * W) | O(W) | Single row DP optimization |

## Typical Pool Allocation Scenario

```csharp
// Load available mortgage securities
var availableAssets = GetMortgageSecuritiesFromDatabase();

// Capital pool for this quarter
decimal capitalPool = 10_000_000; // $10M

var optimizer = new MortgagePoolOptimizer();

// Stage 1: Filter by risk criteria
var screenedAssets = optimizer.FilterByLTV(availableAssets, 0.60m, 0.85m)
    .Where(a => a.InterestRate >= 0.07m && a.InterestRate <= 0.12m)
    .ToList();

// Stage 2: Optimize allocation
var allocation = optimizer.OptimizeZeroOne(screenedAssets, capitalPool);

// Stage 3: Analyze results
Console.WriteLine($"Total Return: ${allocation.MaximumValue:F2}");
Console.WriteLine($"Capital Deployment: {allocation.CapitalUtilizationRate:P1}");
Console.WriteLine($"Weighted Avg Return: {allocation.WeightedAverageReturn:P2}");

// Stage 4: Investment execution
foreach (var asset in allocation.SelectedAssets)
{
    ExecuteInvestment(asset);
}
```

## Real-World Applications

- **Portfolio Rebalancing**: Optimize quarterly capital deployment across available securities
- **Fund Management**: Allocate capital pools across multiple investment opportunities
- **Risk Management**: Constrain selections based on LTV, duration, and security type
- **Return Maximization**: Find the optimal mix of high/low yield investments
- **Liquidity Planning**: Determine best allocation given capital constraints

## Performance Notes

- For pools with <200 assets and <$50M capacity (50 million cents), DP solutions are fast
- For very large pools or assets, use greedy approximation as preprocessing filter
- Bounded knapsack can expand significantly (especially with high max quantities)
- Consider distributing large optimizations across multiple solves with filtered asset subsets

## Testing

Comprehensive test suite included:
- 83+ unit tests covering all three algorithm variants
- Edge cases: empty lists, insufficient capacity, single assets
- Integration tests comparing algorithm results
- Performance tests for large asset pools

Run tests:
```bash
dotnet test
```
