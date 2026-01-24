using Xunit;
using MyDotNetApp.Optimization;

namespace MyDotNetApp.Tests
{
    [Collection("KnapsackMortgageCollection")]
    public class DynamicProgrammingSolverTests
    {
        private readonly MortgagesDataFixture _fixture;
        public DynamicProgrammingSolverTests(MortgagesDataFixture fixture) => _fixture = fixture;

        [Fact]
        public void DpSolver_ReturnsOptimalWithinCapacity()
        {
            var solver = new ZeroOneKnapsackSolver();
            var result = solver.Solve(_fixture.Assets, _fixture.Capacity);
            Assert.True(result.TotalCapitalUsed <= _fixture.Capacity);
            Assert.NotEmpty(result.SelectedAssets);
        }
    }

    [Collection("KnapsackMortgageCollection")]
    public class BruteForceSolverTests
    {
        private readonly MortgagesDataFixture _fixture;
        public BruteForceSolverTests(MortgagesDataFixture fixture) => _fixture = fixture;

        [Fact]
        public void BruteForce_MatchesDpOptimal()
        {
            var dp = new ZeroOneKnapsackSolver().Solve(_fixture.Assets, _fixture.Capacity);
            var brute = new BruteForceKnapsackSolver().Solve(_fixture.Assets, _fixture.Capacity);
            Assert.Equal(dp.MaximumValue, brute.MaximumValue);
        }
    }

    [Collection("KnapsackMortgageCollection")]
    public class MeetInTheMiddleSolverTests
    {
        private readonly MortgagesDataFixture _fixture;
        public MeetInTheMiddleSolverTests(MortgagesDataFixture fixture) => _fixture = fixture;

        [Fact]
        public void MeetInTheMiddle_MatchesDpOptimal()
        {
            var dp = new ZeroOneKnapsackSolver().Solve(_fixture.Assets, _fixture.Capacity);
            var mitm = new MeetInTheMiddleKnapsackSolver().Solve(_fixture.Assets, _fixture.Capacity);
            Assert.Equal(dp.MaximumValue, mitm.MaximumValue);
        }
    }

    [Collection("KnapsackMortgageCollection")]
    public class BranchAndBoundSolverTests
    {
        private readonly MortgagesDataFixture _fixture;
        public BranchAndBoundSolverTests(MortgagesDataFixture fixture) => _fixture = fixture;

        [Fact]
        public void BranchAndBound_MatchesDpOptimal()
        {
            var dp = new ZeroOneKnapsackSolver().Solve(_fixture.Assets, _fixture.Capacity);
            var bab = new BranchAndBoundKnapsackSolver().Solve(_fixture.Assets, _fixture.Capacity);
            Assert.Equal(dp.MaximumValue, bab.MaximumValue);
        }
    }

    [Collection("KnapsackMortgageCollection")]
    public class ApproximateSolverTests
    {
        private readonly MortgagesDataFixture _fixture;
        public ApproximateSolverTests(MortgagesDataFixture fixture) => _fixture = fixture;

        [Theory]
        [InlineData(0.1)]
        [InlineData(0.2)]
        public void Approximate_IsWithinEpsilon(double epsilon)
        {
            var dp = new ZeroOneKnapsackSolver().Solve(_fixture.Assets, _fixture.Capacity);
            var approx = new ApproximateKnapsackSolver().Solve(_fixture.Assets, _fixture.Capacity, epsilon);
            Assert.True(approx.MaximumValue >= dp.MaximumValue * (1 - (decimal)epsilon));
        }
    }

    [Collection("KnapsackMortgageCollection")]
    public class GreedySolverTests
    {
        private readonly MortgagesDataFixture _fixture;
        public GreedySolverTests(MortgagesDataFixture fixture) => _fixture = fixture;

        [Fact]
        public void Greedy_IsFeasible()
        {
            var greedy = new ZeroOneKnapsackSolver().SolveGreedy(_fixture.Assets, _fixture.Capacity);
            Assert.True(greedy.TotalCapitalUsed <= _fixture.Capacity);
            Assert.NotEmpty(greedy.SelectedAssets);
        }
    }
}
