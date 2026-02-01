using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MyDotNetApp.Services;

namespace MyDotNetApp.Controllers;

/// <summary>
/// Controller to demonstrate encrypted Avro message production
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EncryptedMessagesController : ControllerBase
{
    private readonly ILogger<EncryptedMessagesController> _logger;
    private readonly IAvroKafkaProducerWithCSFLE _avroProducer;

    public EncryptedMessagesController(
        ILogger<EncryptedMessagesController> logger,
        IAvroKafkaProducerWithCSFLE avroProducer)
    {
        _logger = logger;
        _avroProducer = avroProducer;
    }

    /// <summary>
    /// Send an encrypted message to Kafka
    /// </summary>
    /// <param name="request">Message request</param>
    [HttpPost("send")]
    public async Task<IActionResult> SendEncryptedMessage([FromBody] EncryptedMessageRequest request)
    {
        try
        {
            var result = await _avroProducer.ProduceAsync(
                topic: request.Topic ?? "encrypted-avro-messages",
                key: request.Key ?? Guid.NewGuid().ToString(),
                payload: request.Payload,
                eventType: request.EventType,
                metadata: request.Metadata
            );

            return Ok(new
            {
                success = true,
                topic = result.Topic,
                partition = result.Partition.Value,
                offset = result.Offset.Value,
                timestamp = result.Timestamp.UnixTimestampMs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send encrypted message");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Send batch of encrypted messages
    /// </summary>
    [HttpPost("send-batch")]
    public async Task<IActionResult> SendBatchEncryptedMessages([FromBody] BatchMessageRequest request)
    {
        var results = new List<object>();
        var errors = new List<string>();

        foreach (var msg in request.Messages)
        {
            try
            {
                var result = await _avroProducer.ProduceAsync(
                    topic: msg.Topic ?? "encrypted-avro-messages",
                    key: msg.Key ?? Guid.NewGuid().ToString(),
                    payload: msg.Payload,
                    eventType: msg.EventType,
                    metadata: msg.Metadata
                );

                results.Add(new
                {
                    success = true,
                    key = msg.Key,
                    partition = result.Partition.Value,
                    offset = result.Offset.Value
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message with key {Key}", msg.Key);
                errors.Add($"Key {msg.Key}: {ex.Message}");
            }
        }

        return Ok(new
        {
            totalSent = results.Count,
            totalFailed = errors.Count,
            results,
            errors
        });
    }

    /// <summary>
    /// Health check
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", service = "EncryptedMessagesController" });
    }
}

public class EncryptedMessageRequest
{
    public string? Topic { get; set; }
    public string? Key { get; set; }
    public required object Payload { get; set; }
    public required string EventType { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public class BatchMessageRequest
{
    public required List<EncryptedMessageRequest> Messages { get; set; }
}
