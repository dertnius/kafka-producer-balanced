using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using MyDotNetApp.Optimization;

namespace MyDotNetApp.Tests.Optimization
{
    public class KnapsackAlgorithmComparison
    {
        private List<MortgageAsset> CreateLargeAssetPool()
        {
            var assets = new List<MortgageAsset>();
            var securityTypes = new[] { "Residential", "Commercial", "Industrial" };
            var random = new Random(42); // Seed for reproducibility

            for (int i = 1; i <= 25; i++)
            {
                assets.Add(new MortgageAsset
                {
                    Id = i,
                    Name = $"Mortgage Asset {i}",
                    // Keep numbers small to avoid huge DP tables in tests
                    CapitalRequired = random.Next(200, 900),
                    ExpectedReturn = random.Next(40, 300),
                    InterestRate = (decimal)random.Next(5, 12) / 100,
                    TermMonths = random.Next(120, 360),
                    LoanToValueRatio = (decimal)random.Next(60, 90) / 100,
                    SecurityType = securityTypes[random.Next(securityTypes.Length)]
                });
            }

            return assets;
        }

        [Fact]
        public void CompareZeroOneVsGreedy()
        {
            // Arrange
            var solver = new ZeroOneKnapsackSolver();
            var assets = CreateLargeAssetPool();
            decimal poolSize = 5_000; // smaller to keep DP memory modest

            // Act
            var dpResult = solver.Solve(assets, poolSize);
            var greedyResult = solver.SolveGreedy(assets, poolSize);

            // Assert
            // DP should always equal or exceed greedy for optimal solution
            Assert.True(dpResult.MaximumValue >= greedyResult.MaximumValue * 0.95m);
            Assert.NotEmpty(dpResult.SelectedAssets);
            Assert.NotEmpty(greedyResult.SelectedAssets);
        }

        [Fact]
        public void BoundedKnapsack_AllowsMultipleUnitsPerAsset()
        {
            // Arrange
            var assets = new List<MortgageAsset>
            {
                new MortgageAsset
                {
                    Id = 1,
                    Name = "Standard Mortgage",
                    CapitalRequired = 200_000,
                    ExpectedReturn = 20_000,
                    InterestRate = 0.10m,
                    TermMonths = 360,
                    LoanToValueRatio = 0.80m,
                    SecurityType = "Residential"
                }
            };
            
            var quantities = new Dictionary<int, int> { { 1, 5 } }; // Can select up to 5 times
            decimal poolSize = 1_500_000;
            
            var solver = new BoundedKnapsackSolver();

            // Act
            var result = solver.Solve(assets, quantities, poolSize);

            // Assert
            Assert.NotEmpty(result.SelectedAssets);
            Assert.True(result.AssetSelectionMap[1] > 1); // Multiple units selected
        }

        [Fact]
        public void UnboundedKnapsack_SelectsSameAssetMultipleTimes()
        {
            // Arrange
            var assets = new List<MortgageAsset>
            {
                new MortgageAsset
                {
                    Id = 1,
                    Name = "High Return Mortgage",
                    CapitalRequired = 100_000,
                    ExpectedReturn = 15_000,
                    InterestRate = 0.15m,
                    TermMonths = 240,
                    LoanToValueRatio = 0.75m,
                    SecurityType = "Commercial"
                },
                new MortgageAsset
                {
                    Id = 2,
                    Name = "Standard Mortgage",
                    CapitalRequired = 200_000,
                    ExpectedReturn = 16_000,
                    InterestRate = 0.08m,
                    TermMonths = 360,
                    LoanToValueRatio = 0.80m,
                    SecurityType = "Residential"
                }
            };

            decimal poolSize = 500_000;
            var solver = new UnboundedKnapsackSolver();

            // Act
            var result = solver.Solve(assets, poolSize);

            // Assert
            Assert.NotEmpty(result.SelectedAssets);
            Assert.True(result.TotalCapitalUsed <= poolSize);
            Assert.True(result.MaximumValue > 0);
        }

        [Fact]
        public void MortgageAsset_CalculatesROICorrectly()
        {
            // Arrange
            var asset = new MortgageAsset
            {
                Id = 1,
                Name = "Test",
                CapitalRequired = 100_000,
                ExpectedReturn = 10_000,
                InterestRate = 0.10m,
                TermMonths = 360,
                LoanToValueRatio = 0.80m,
                SecurityType = "Residential"
            };

            // Act
            var roi = asset.GetROIRatio();
            var annual = asset.GetAnnualReturn();

            // Assert
            Assert.Equal(0.1m, roi);
            Assert.Equal(10_000m, annual);
        }

        [Fact]
        public void KnapsackResult_CalculatesDerivedMetrics()
        {
            // Arrange
            var result = new KnapsackResult
            {
                CapitalAvailable = 500_000,
                SelectedAssets = new List<MortgageAsset>
                {
                    new MortgageAsset
                    {
                        Id = 1,
                        CapitalRequired = 200_000,
                        ExpectedReturn = 20_000
                    },
                    new MortgageAsset
                    {
                        Id = 2,
                        CapitalRequired = 150_000,
                        ExpectedReturn = 15_000
                    }
                }
            };

            // Act
            result.CalculateDerivedMetrics();

            // Assert
            Assert.Equal(350_000, result.TotalCapitalUsed);
            Assert.Equal(150_000, result.CapitalUnused);
            Assert.Equal(0.7m, result.CapitalUtilizationRate);
            Assert.Equal(35_000m / 350_000m, result.WeightedAverageReturn);
        }

        [Fact]
        public void OptimizationPreservesAssetProperties()
        {
            // Arrange
            var asset = new MortgageAsset
            {
                Id = 42,
                Name = "Premium Commercial",
                CapitalRequired = 500_000,
                ExpectedReturn = 65_000,
                InterestRate = 0.13m,
                TermMonths = 240,
                LoanToValueRatio = 0.70m,
                SecurityType = "Commercial"
            };

            var assets = new List<MortgageAsset> { asset };
            var solver = new ZeroOneKnapsackSolver();

            // Act
            var result = solver.Solve(assets, 1_000_000);

            // Assert
            var selectedAsset = result.SelectedAssets.First();
            Assert.Equal(asset.Id, selectedAsset.Id);
            Assert.Equal(asset.Name, selectedAsset.Name);
            Assert.Equal(asset.InterestRate, selectedAsset.InterestRate);
            Assert.Equal(asset.SecurityType, selectedAsset.SecurityType);
        }
    }
}
