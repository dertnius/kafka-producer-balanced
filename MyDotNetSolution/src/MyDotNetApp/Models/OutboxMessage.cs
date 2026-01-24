using MyDotNetApp.Data.Attributes;
using System;

namespace MyDotNetApp.Models
{
    /// <summary>
    /// Entity representing a message in the outbox pattern
    /// </summary>
    [Table("OutboxMessage")]
    public class OutboxMessage
    {
        [Key]
        public int MessageId { get; set; }

        public string Topic { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        public bool IsPublished { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? PublishedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public int RetryCount { get; set; }
    }
}
