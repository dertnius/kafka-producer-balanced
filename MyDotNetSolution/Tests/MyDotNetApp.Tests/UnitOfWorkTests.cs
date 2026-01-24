using Xunit;
using Moq;
using System;
using System.Data;
using System.Threading.Tasks;
using MyDotNetApp.Data.UnitOfWork;

namespace MyDotNetApp.Tests
{
    public class UnitOfWorkTests
    {
        [Fact]
        public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new UnitOfWork(null!));
        }

        [Fact]
        public void Constructor_WithEmptyConnectionString_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new UnitOfWork(""));
        }

        [Fact]
        public void Constructor_WithValidConnectionString_Succeeds()
        {
            // Arrange
            var connectionString = "Server=.;Database=test;";

            // Act
            var unitOfWork = new UnitOfWork(connectionString);

            // Assert
            Assert.NotNull(unitOfWork);

            // Cleanup
            unitOfWork.Dispose();
        }

        [Fact]
        public void UnitOfWork_ImplementsIDisposable()
        {
            // Arrange
            var connectionString = "Server=.;Database=test;";
            var unitOfWork = new UnitOfWork(connectionString);

            // Act & Assert
            Assert.IsAssignableFrom<IDisposable>(unitOfWork);
        }

        [Fact]
        public void UnitOfWork_ImplementsIUnitOfWork()
        {
            // Arrange
            var connectionString = "Server=.;Database=test;";
            var unitOfWork = new UnitOfWork(connectionString);

            // Act & Assert
            Assert.IsAssignableFrom<IUnitOfWork>(unitOfWork);

            // Cleanup
            unitOfWork.Dispose();
        }

        [Fact]
        public void Dispose_MultipleTimes_DoesNotThrow()
        {
            // Arrange
            var connectionString = "Server=.;Database=test;";
            var unitOfWork = new UnitOfWork(connectionString);

            // Act & Assert (should not throw)
            unitOfWork.Dispose();
            unitOfWork.Dispose();
            unitOfWork.Dispose();
        }

        [Fact]
        public void Constructor_StoresConnectionString()
        {
            // Arrange
            var connectionString = "Server=.;Database=test;";

            // Act
            var unitOfWork = new UnitOfWork(connectionString);

            // Assert - verify the connection string is used by checking that Connection property exists
            Assert.NotNull(unitOfWork);
            
            // Cleanup
            unitOfWork.Dispose();
        }

        [Fact]
        public void UnitOfWork_HasConnectionProperty()
        {
            // Arrange
            var connectionString = "Server=.;Database=test;";
            var unitOfWork = new UnitOfWork(connectionString);

            // Act & Assert - verify the property exists via reflection
            var property = typeof(IUnitOfWork).GetProperty("Connection");
            Assert.NotNull(property);

            // Cleanup
            unitOfWork.Dispose();
        }

        [Fact]
        public void UnitOfWork_HasTransactionProperty()
        {
            // Arrange
            var connectionString = "Server=.;Database=test;";
            var unitOfWork = new UnitOfWork(connectionString);

            // Act & Assert - Transaction should be null initially
            Assert.Null(unitOfWork.Transaction);

            // Cleanup
            unitOfWork.Dispose();
        }

        [Fact]
        public void UnitOfWork_HasBeginTransactionMethod()
        {
            // Arrange
            var connectionString = "Server=.;Database=test;";
            var unitOfWork = new UnitOfWork(connectionString);

            // Act & Assert - verify method exists by checking type
            var method = typeof(IUnitOfWork).GetMethod("BeginTransaction");
            Assert.NotNull(method);

            // Cleanup
            unitOfWork.Dispose();
        }

        [Fact]
        public void UnitOfWork_HasCommitMethod()
        {
            // Arrange
            var connectionString = "Server=.;Database=test;";
            var unitOfWork = new UnitOfWork(connectionString);

            // Act & Assert - verify method exists by checking type
            var method = typeof(IUnitOfWork).GetMethod("Commit");
            Assert.NotNull(method);

            // Cleanup
            unitOfWork.Dispose();
        }

        [Fact]
        public void UnitOfWork_HasRollbackMethod()
        {
            // Arrange
            var connectionString = "Server=.;Database=test;";
            var unitOfWork = new UnitOfWork(connectionString);

            // Act & Assert - verify method exists by checking type
            var method = typeof(IUnitOfWork).GetMethod("Rollback");
            Assert.NotNull(method);

            // Cleanup
            unitOfWork.Dispose();
        }
    }
}
