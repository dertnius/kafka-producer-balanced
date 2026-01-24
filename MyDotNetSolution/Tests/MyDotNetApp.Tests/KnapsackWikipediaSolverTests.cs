using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using MyDotNetApp.Optimization;

namespace MyDotNetApp.Tests
{
    [Collection("KnapsackMortgageCollection")]
    public class KnapsackWikipediaSolverTests
    {
        private readonly MortgagesDataFixture _fixture;

        public KnapsackWikipediaSolverTests(MortgagesDataFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void AllExactSolvers_ReturnSameOptimalValue()
        {
            // Arrange
            var assets = _fixture.Assets;
            decimal capacity = _fixture.Capacity; // pool size

            var dpSolver = new ZeroOneKnapsackSolver();
            var bruteSolver = new BruteForceKnapsackSolver();
            var mitmSolver = new MeetInTheMiddleKnapsackSolver();
            var babSolver = new BranchAndBoundKnapsackSolver();

            // Act
            var dp = dpSolver.Solve(assets, capacity);
            var brute = bruteSolver.Solve(assets, capacity);
            var mitm = mitmSolver.Solve(assets, capacity);
            var bab = babSolver.Solve(assets, capacity);

            // Assert
            Assert.Equal(dp.MaximumValue, brute.MaximumValue);
            Assert.Equal(dp.MaximumValue, mitm.MaximumValue);
            Assert.Equal(dp.MaximumValue, bab.MaximumValue);
            Assert.True(dp.TotalCapitalUsed <= capacity);
        }

        [Fact]
        public void ApproximateSolver_IsWithinEpsilon()
        {
            // Arrange
            var assets = _fixture.Assets;
            decimal capacity = _fixture.Capacity;
            double epsilon = 0.1; // 10%

            var dpSolver = new ZeroOneKnapsackSolver();
            var fptas = new ApproximateKnapsackSolver();

            // Act
            var optimal = dpSolver.Solve(assets, capacity);
            var approx = fptas.Solve(assets, capacity, epsilon);

            // Assert
            Assert.True(approx.MaximumValue >= optimal.MaximumValue * (1 - (decimal)epsilon));
            Assert.True(approx.TotalCapitalUsed <= capacity);
        }

        [Fact]
        public void Greedy_IsFeasible_NotOptimalCheck()
        {
            // Arrange
            var assets = _fixture.Assets;
            decimal capacity = _fixture.Capacity;
            var solver = new ZeroOneKnapsackSolver();

            // Act
            var greedy = solver.SolveGreedy(assets, capacity);

            // Assert
            Assert.True(greedy.TotalCapitalUsed <= capacity);
            Assert.NotEmpty(greedy.SelectedAssets);
        }
    }
}
