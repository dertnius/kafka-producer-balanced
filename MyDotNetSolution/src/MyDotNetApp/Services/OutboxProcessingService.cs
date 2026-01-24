using MyDotNetApp.Data.Concrete;
using MyDotNetApp.Data.UnitOfWork;
using MyDotNetApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MyDotNetApp.Services
{
    /// <summary>
    /// Service for handling outbox message processing
    /// </summary>
    public class OutboxProcessingService
    {
        private readonly IOutboxReadRepository _readRepo;
        private readonly IOutboxWriteRepository _writeRepo;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<OutboxProcessingService> _logger;

        public OutboxProcessingService(
            IOutboxReadRepository readRepo,
            IOutboxWriteRepository writeRepo,
            IUnitOfWork unitOfWork,
            ILogger<OutboxProcessingService> logger)
        {
            _readRepo = readRepo;
            _writeRepo = writeRepo;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <summary>
        /// Processes pending outbox messages atomically
        /// </summary>
        public async Task ProcessPendingMessagesAsync(int batchSize = 100)
        {
            _unitOfWork.BeginTransaction();
            try
            {
                // Read pending messages (no transaction needed for reads)
                var messages = await _readRepo.GetPendingMessagesAsync(batchSize);
                var messageList = messages.ToList();

                if (!messageList.Any())
                {
                    _logger.LogInformation("No pending messages to process");
                    return;
                }

                _logger.LogInformation($"Processing {messageList.Count} pending messages");

                foreach (var message in messageList)
                {
                    try
                    {
                        // Process message (publish to Kafka, etc.)
                        await PublishMessageAsync(message);

                        // Mark as published within the transaction
                        await _writeRepo.MarkAsPublishedAsync(message.MessageId);

                        _logger.LogInformation($"Published message {message.MessageId} to topic {message.Topic}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to process message {message.MessageId}");
                        throw; // Rollback transaction
                    }
                }

                _unitOfWork.Commit();
                _logger.LogInformation($"Successfully committed {messageList.Count} messages");
            }
            catch (Exception ex)
            {
                _unitOfWork.Rollback();
                _logger.LogError(ex, "Error processing outbox messages. Transaction rolled back.");
                throw;
            }
        }

        /// <summary>
        /// Gets all published messages within a date range
        /// </summary>
        public async Task<IEnumerable<Models.OutboxMessage>> GetPublishedMessagesAsync(DateTime from, DateTime to)
        {
            return await _readRepo.GetPublishedMessagesAsync(from, to);
        }

        /// <summary>
        /// Inserts a new outbox message
        /// </summary>
        public async Task<int> InsertMessageAsync(Models.OutboxMessage message)
        {
            _unitOfWork.BeginTransaction();
            try
            {
                var id = await _writeRepo.InsertAsync(message);
                _unitOfWork.Commit();
                return (int)id;
            }
            catch
            {
                _unitOfWork.Rollback();
                throw;
            }
        }

        private async Task PublishMessageAsync(Models.OutboxMessage message)
        {
            // TODO: Implement actual message publishing logic (Kafka, etc.)
            await Task.Delay(100); // Simulate publishing
        }
    }
}
