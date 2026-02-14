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
        if (request == null)
        {
            return BadRequest(new { success = false, error = "Request cannot be null" });
        }

        if (request.Payload == null)
        {
            return BadRequest(new { success = false, error = "Payload cannot be null" });
        }

        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            return BadRequest(new { success = false, error = "EventType is required" });
        }

        try
        {
            // Verify producer is injected
            if (_avroProducer == null)
            {
                _logger.LogError("❌ CRITICAL: IAvroKafkaProducerWithCSFLE is NULL - Not registered in DI!");
                return StatusCode(500, new 
                { 
                    success = false, 
                    error = "Producer not initialized",
                    details = "IAvroKafkaProducerWithCSFLE is not registered in dependency injection. Add services.AddSingleton<IAvroKafkaProducerWithCSFLE, AvroKafkaProducerWithCSFLE>() to Startup.cs"
                });
            }

            _logger.LogInformation("📤 Calling ProduceAsync: Key={Key}, EventType={EventType}", 
                request.Key ?? "auto-generated", request.EventType);

            var result = await _avroProducer.ProduceAsync(
                topic: request.Topic ?? "encrypted-avro-messages",
                key: request.Key ?? Guid.NewGuid().ToString(),
                payload: request.Payload,
                eventType: request.EventType,
                metadata: request.Metadata
            );

            // Verify result is not null
            if (result == null)
            {
                _logger.LogError("❌ ProduceAsync returned NULL! This should never happen.");
                return StatusCode(500, new 
                { 
                    success = false, 
                    error = "Producer returned null result",
                    details = "The Kafka producer returned a null DeliveryResult, which indicates an internal error"
                });
            }

            _logger.LogInformation("✅ Message produced successfully: Topic={Topic}, Partition={Partition}, Offset={Offset}",
                result.Topic, result.Partition.Value, result.Offset.Value);

            return Ok(new
            {
                success = true,
                topic = result.Topic,
                partition = result.Partition.Value,
                offset = result.Offset.Value,
                timestamp = result.Timestamp.UnixTimestampMs
            });
        }
        catch (ArgumentException argEx)
        {
            _logger.LogError(argEx, "❌ VALIDATION ERROR: {Message}", argEx.Message);
            return BadRequest(new { success = false, error = argEx.Message });
        }
        catch (InvalidOperationException opEx)
        {
            _logger.LogError(opEx, "❌ OPERATION ERROR: {Message}", opEx.Message);
            return StatusCode(500, new { success = false, error = opEx.Message });
        }
        catch (AggregateException aggEx)
        {
            _logger.LogError(aggEx, "❌ AGGREGATE ERROR: {Message}", string.Join("; ", aggEx.InnerExceptions.Select(e => e.Message)));
            return StatusCode(500, new 
            { 
                success = false, 
                error = "One or more errors occurred during processing",
                details = string.Join("; ", aggEx.InnerExceptions.Select(e => $"{e.GetType().Name}: {e.Message}"))
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ UNEXPECTED ERROR: {ExceptionType}: {Message}\n{StackTrace}", 
                ex.GetType().Name, ex.Message, ex.StackTrace);
            return StatusCode(500, new 
            { 
                success = false, 
                error = ex.Message,
                exceptionType = ex.GetType().Name,
                details = $"{ex.GetType().FullName}: {ex.Message}. Check application logs for full stack trace."
            });
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
    /// Diagnostic endpoint to check producer status
    /// </summary>
    [HttpGet("diagnostics")]
    public IActionResult Diagnostics()
    {
        var diagnostics = new
        {
            timestamp = DateTimeOffset.UtcNow,
            producerStatus = _avroProducer == null ? "NOT_REGISTERED" : "REGISTERED",
            producerHealth = _avroProducer?.IsHealthy() ?? false ? "HEALTHY" : "UNHEALTHY",
            checks = new[]
            {
                new
                {
                    check = "Producer Dependency Injection",
                    status = _avroProducer == null ? "FAIL" : "PASS",
                    message = _avroProducer == null 
                        ? "IAvroKafkaProducerWithCSFLE not registered. Add services.AddSingleton<IAvroKafkaProducerWithCSFLE, AvroKafkaProducerWithCSFLE>() to Startup.cs ConfigureServices()"
                        : "Producer is properly injected"
                },
                new
                {
                    check = "Configuration",
                    status = "INFO",
                    message = "Check appsettings.json for KafkaOutboxSettings and AzureKeyVault sections"
                },
                new
                {
                    check = "Error Handling",
                    status = "INFO",
                    message = "If ProduceAsync returns null, check application logs for detailed exception information"
                }
            },
            troubleshooting = new
            {
                issue = "ProduceAsync returns null",
                causes = new[]
                {
                    "Azure Key Vault authentication failed (check Azure credentials)",
                    "Schema Registry connection failed (check if Confluent Schema Registry is running)",
                    "Kafka broker connection failed (check if Kafka is running)",
                    "Message serialization failed (check payload format)"
                },
                solution = "Check application logs in 'logs/' directory for detailed error messages"
            }
        };

        _logger.LogInformation("📊 Diagnostics requested: {@Diagnostics}", diagnostics);
        return Ok(diagnostics);
    }

    /// <summary>
    /// Health check
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        var isHealthy = _avroProducer?.IsHealthy() ?? false;
        return isHealthy 
            ? Ok(new { status = "healthy", service = "EncryptedMessagesController" })
            : StatusCode(503, new { status = "unhealthy", service = "EncryptedMessagesController", message = "Producer not healthy" });
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
