using MyDotNetApp.Data.Attributes;
using MyDotNetApp.Data.Repositories;
using MyDotNetApp.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace MyDotNetApp.Data.Concrete
{
    /// <summary>
    /// Read repository for OutboxMessage entities
    /// </summary>
    public interface IOutboxReadRepository : IReadRepository<OutboxMessage>
    {
        /// <summary>
        /// Gets pending messages that haven't been published yet
        /// </summary>
        Task<IEnumerable<OutboxMessage>> GetPendingMessagesAsync(int batchSize);

        /// <summary>
        /// Gets published messages within a date range
        /// </summary>
        Task<IEnumerable<OutboxMessage>> GetPublishedMessagesAsync(DateTime from, DateTime to);
    }

    public class OutboxReadRepository : ReadRepository<OutboxMessage>, IOutboxReadRepository
    {
        public OutboxReadRepository(System.Data.IDbConnection connection) : base(connection)
        {
        }

        public async Task<IEnumerable<OutboxMessage>> GetPendingMessagesAsync(int batchSize)
        {
            var sql = @"
                SELECT TOP (@BatchSize) * 
                FROM OutboxMessage 
                WHERE IsPublished = 0 
                ORDER BY CreatedAt ASC";

            return await QueryAsync(sql, new { BatchSize = batchSize });
        }

        public async Task<IEnumerable<OutboxMessage>> GetPublishedMessagesAsync(DateTime from, DateTime to)
        {
            var sql = @"
                SELECT * 
                FROM OutboxMessage 
                WHERE IsPublished = 1 
                AND PublishedAt BETWEEN @From AND @To
                ORDER BY PublishedAt DESC";

            return await QueryAsync(sql, new { From = from, To = to });
        }
    }

    /// <summary>
    /// Write repository for OutboxMessage entities
    /// </summary>
    public interface IOutboxWriteRepository : IWriteRepository<OutboxMessage>
    {
        /// <summary>
        /// Marks a message as published
        /// </summary>
        Task MarkAsPublishedAsync(int messageId);

        /// <summary>
        /// Marks multiple messages as published
        /// </summary>
        Task MarkMultipleAsPublishedAsync(params int[] messageIds);
    }

    public class OutboxWriteRepository : WriteRepository<OutboxMessage>, IOutboxWriteRepository
    {
        public OutboxWriteRepository(UnitOfWork.IUnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public async Task MarkAsPublishedAsync(int messageId)
        {
            var sql = @"
                UPDATE OutboxMessage 
                SET IsPublished = 1, PublishedAt = @PublishedAt, UpdatedAt = @UpdatedAt
                WHERE MessageId = @MessageId";

            await ExecuteAsync(sql, new
            {
                MessageId = messageId,
                PublishedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        public async Task MarkMultipleAsPublishedAsync(params int[] messageIds)
        {
            if (messageIds.Length == 0)
                return;

            var placeholders = string.Join(",", messageIds.Select((_, i) => $"@Id{i}"));
            var sql = $@"
                UPDATE OutboxMessage 
                SET IsPublished = 1, PublishedAt = @PublishedAt, UpdatedAt = @UpdatedAt
                WHERE MessageId IN ({placeholders})";

            var parameters = new Dapper.DynamicParameters();
            parameters.Add("@PublishedAt", DateTime.UtcNow);
            parameters.Add("@UpdatedAt", DateTime.UtcNow);

            for (int i = 0; i < messageIds.Length; i++)
            {
                parameters.Add($"@Id{i}", messageIds[i]);
            }

            await ExecuteAsync(sql, parameters);
        }
    }
}
