using Confluent.Kafka;
using Dapper;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MyDotNetApp.Tests
{
    public class HomeBackgroundServiceTests : IDisposable
    {
        private readonly Mock<ILogger<HomeBackgroundService>> _loggerMock;
        private readonly Mock<IDbConnection> _dbConnectionMock;
        private readonly Mock<IProducer<string, string>> _kafkaProducerMock;
        private readonly HomeBackgroundService _service;
        private bool _disposed = false;

        public HomeBackgroundServiceTests()
        {
            _loggerMock = new Mock<ILogger<HomeBackgroundService>>();
            _dbConnectionMock = new Mock<IDbConnection>();
            _kafkaProducerMock = new Mock<IProducer<string, string>>();

            _service = new HomeBackgroundService(
                _loggerMock.Object,
                _dbConnectionMock.Object,
                _kafkaProducerMock.Object
            );
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _loggerMock?.Reset();
                    _dbConnectionMock?.Reset();
                    _kafkaProducerMock?.Reset();
                    _service?.Dispose();
                }
                _disposed = true;
            }
        }

        [Fact]
        public async Task AllMessagesSuccessfullyProcessed()
        {
            // Arrange
            var outboxItems = new List<OutboxItem>
            {
                new OutboxItem { Id = 1, Stid = "A", Code = "", Rank = 1, Processed = false },
                new OutboxItem { Id = 2, Stid = "A", Code = "Code1", Rank = 2, Processed = false }
            };

            _dbConnectionMock
                .Setup(db => db.QueryAsync<OutboxItem>(It.IsAny<string>(), null, null, null, null))
                .ReturnsAsync(outboxItems);

            _kafkaProducerMock
                .Setup(producer => producer.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _dbConnectionMock
                .Setup(db => db.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), null, null, null))
                .ReturnsAsync(1);

            // Act
            await _service.ExecuteAsync(CancellationToken.None);

            // Assert
            _dbConnectionMock.Verify(db => db.ExecuteAsync(
                "UPDATE Outbox SET Processed = 1, Processing = 0 WHERE Id = @Id",
                It.IsAny<object>(),
                null, null, null), Times.Exactly(2));
        }

        [Fact]
        public async Task MessageFailsInGroup_MarksAllAsNotPublished()
        {
            // Arrange
            var outboxItems = new List<OutboxItem>
            {
                new OutboxItem { Id = 1, Stid = "A", Code = "", Rank = 1, Processed = false },
                new OutboxItem { Id = 2, Stid = "A", Code = "Code1", Rank = 2, Processed = false }
            };

            _dbConnectionMock
                .Setup(db => db.QueryAsync<OutboxItem>(It.IsAny<string>(), null, null, null, null))
                .ReturnsAsync(outboxItems);

            _kafkaProducerMock
                .Setup(producer => producer.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
                .Throws(new Exception("Kafka error"));

            _dbConnectionMock
                .Setup(db => db.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), null, null, null))
                .ReturnsAsync(1);

            // Act
            await _service.ExecuteAsync(CancellationToken.None);

            // Assert
            _dbConnectionMock.Verify(db => db.ExecuteAsync(
                "UPDATE Outbox SET Processed = -1 WHERE Stid = @Stid",
                It.Is<object>(o => ((dynamic)o).Stid == "A"),
                null, null, null), Times.Once);
        }

        [Fact]
        public async Task DeliveryConfirmationFails_MarksAllAsNotPublished()
        {
            // Arrange
            var outboxItems = new List<OutboxItem>
            {
                new OutboxItem { Id = 1, Stid = "A", Code = "", Rank = 1, Processed = false }
            };

            _dbConnectionMock
                .Setup(db => db.QueryAsync<OutboxItem>(It.IsAny<string>(), null, null, null, null))
                .ReturnsAsync(outboxItems);

            _dbConnectionMock
                .Setup(db => db.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), null, null, null))
                .ReturnsAsync(1);

            // Simulate delivery confirmation failure
            _service.GetType()
                .GetMethod("WaitForDeliveryConfirmationAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(_service, new object[] { "A", CancellationToken.None });

            // Act
            await _service.ExecuteAsync(CancellationToken.None);

            // Assert
            _dbConnectionMock.Verify(db => db.ExecuteAsync(
                "UPDATE Outbox SET Processed = -1 WHERE Stid = @Stid",
                It.Is<object>(o => ((dynamic)o).Stid == "A"),
                null, null, null), Times.Once);
        }

        [Fact]
        public async Task ResumesProcessingAfterCrash()
        {
            // Arrange
            var outboxItems = new List<OutboxItem>
            {
                new OutboxItem { Id = 1, Stid = "A", Code = "", Rank = 1, Processed = false, Processing = true },
                new OutboxItem { Id = 2, Stid = "A", Code = "Code1", Rank = 2, Processed = false, Processing = true }
            };

            _dbConnectionMock
                .Setup(db => db.QueryAsync<OutboxItem>(It.IsAny<string>(), null, null, null, null))
                .ReturnsAsync(outboxItems);

            _dbConnectionMock
                .Setup(db => db.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), null, null, null))
                .ReturnsAsync(1);

            // Act
            await _service.ExecuteAsync(CancellationToken.None);

            // Assert
            _dbConnectionMock.Verify(db => db.ExecuteAsync(
                "UPDATE Outbox SET Processed = 1, Processing = 0 WHERE Id = @Id",
                It.IsAny<object>(),
                null, null, null), Times.Exactly(2));
        }
    }
}