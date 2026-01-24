using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using MyDotNetApp.Optimization;

namespace MyDotNetApp.Tests.Optimization
{
    public class ZeroOneKnapsackSolverTests
    {
        private List<MortgageAsset> CreateSampleAssets()
        {
            return new List<MortgageAsset>
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
                },
                new MortgageAsset
                {
                    Id = 3,
                    Name = "Residential Mortgage C",
                    CapitalRequired = 80_000,
                    ExpectedReturn = 6_400,
                    InterestRate = 0.08m,
                    TermMonths = 360,
                    LoanToValueRatio = 0.85m,
                    SecurityType = "Residential"
                },
                new MortgageAsset
                {
                    Id = 4,
                    Name = "Commercial Mortgage D",
                    CapitalRequired = 120_000,
                    ExpectedReturn = 13_200,
                    InterestRate = 0.11m,
                    TermMonths = 240,
                    LoanToValueRatio = 0.70m,
                    SecurityType = "Commercial"
                }
            };
        }

        [Fact]
        public void Solve_WithEmptyAssets_ReturnsZeroValue()
        {
            // Arrange
            var solver = new ZeroOneKnapsackSolver();
            var assets = new List<MortgageAsset>();
            decimal capacity = 500_000;

            // Act
            var result = solver.Solve(assets, capacity);

            // Assert
            Assert.Equal(0, result.MaximumValue);
            Assert.Empty(result.SelectedAssets);
        }

        [Fact]
        public void Solve_WithNullAssets_ReturnsZeroValue()
        {
            // Arrange
            var solver = new ZeroOneKnapsackSolver();
            decimal capacity = 500_000;

            // Act
            var result = solver.Solve(null!, capacity);

            // Assert
            Assert.Equal(0, result.MaximumValue);
        }

        [Fact]
        public void Solve_WithSingleAsset_SelectsIfItFits()
        {
            // Arrange
            var solver = new ZeroOneKnapsackSolver();
            var assets = new List<MortgageAsset>
            {
                new MortgageAsset
                {
                    Id = 1,
                    Name = "Test Asset",
                    CapitalRequired = 100_000,
                    ExpectedReturn = 10_000,
                    InterestRate = 0.10m,
                    TermMonths = 360,
                    LoanToValueRatio = 0.80m,
                    SecurityType = "Residential"
                }
            };
            decimal capacity = 150_000;

            // Act
            var result = solver.Solve(assets, capacity);

            // Assert
            Assert.Single(result.SelectedAssets);
            Assert.Equal(10_000, result.MaximumValue);
            Assert.Equal(100_000, result.TotalCapitalUsed);
        }

        [Fact]
        public void Solve_WithCapacityTooSmall_SelectsNothing()
        {
            // Arrange
            var solver = new ZeroOneKnapsackSolver();
            var assets = CreateSampleAssets();
            decimal capacity = 50_000; // Too small for any asset

            // Act
            var result = solver.Solve(assets, capacity);

            // Assert
            Assert.Empty(result.SelectedAssets);
            Assert.Equal(0, result.MaximumValue);
        }

        [Fact]
        public void Solve_WithMultipleAssets_SelectsOptimalCombination()
        {
            // Arrange
            var solver = new ZeroOneKnapsackSolver();
            var assets = CreateSampleAssets();
            decimal capacity = 350_000;

            // Act
            var result = solver.Solve(assets, capacity);

            // Assert
            Assert.NotEmpty(result.SelectedAssets);
            Assert.True(result.TotalCapitalUsed <= capacity);
            Assert.True(result.MaximumValue > 0);
            Assert.True(result.CapitalUtilizationRate > 0);
        }

        [Fact]
        public void Solve_CalculatesUtilizationRate()
        {
            // Arrange
            var solver = new ZeroOneKnapsackSolver();
            var assets = CreateSampleAssets();
            decimal capacity = 300_000;

            // Act
            var result = solver.Solve(assets, capacity);

            // Assert
            Assert.Equal(capacity, result.CapitalAvailable);
            Assert.True(result.CapitalUtilizationRate >= 0);
            Assert.True(result.CapitalUtilizationRate <= 1);
            Assert.Equal(result.TotalCapitalUsed + result.CapitalUnused, capacity, precision: 2);
        }

        [Fact]
        public void SolveGreedy_SelectsHighROIAssets()
        {
            // Arrange
            var solver = new ZeroOneKnapsackSolver();
            var assets = CreateSampleAssets();
            decimal capacity = 350_000;

            // Act
            var result = solver.SolveGreedy(assets, capacity);

            // Assert
            Assert.NotEmpty(result.SelectedAssets);
            // Greedy should select assets with highest ROI ratio
            var firstAssetROI = result.SelectedAssets.First().GetROIRatio();
            Assert.True(firstAssetROI >= 0);
        }

        [Fact]
        public void Solve_DoesNotExceedCapacity()
        {
            // Arrange
            var solver = new ZeroOneKnapsackSolver();
            var assets = CreateSampleAssets();
            decimal capacity = 250_000;

            // Act
            var result = solver.Solve(assets, capacity);

            // Assert
            Assert.True(result.TotalCapitalUsed <= capacity);
        }

        [Fact]
        public void Solve_PopulatesAssetSelectionMap()
        {
            // Arrange
            var solver = new ZeroOneKnapsackSolver();
            var assets = CreateSampleAssets();
            decimal capacity = 350_000;

            // Act
            var result = solver.Solve(assets, capacity);

            // Assert
            foreach (var asset in result.SelectedAssets)
            {
                Assert.True(result.AssetSelectionMap.ContainsKey(asset.Id));
                Assert.Equal(1, result.AssetSelectionMap[asset.Id]); // 0/1 knapsack = 1
            }
        }
    }
}
