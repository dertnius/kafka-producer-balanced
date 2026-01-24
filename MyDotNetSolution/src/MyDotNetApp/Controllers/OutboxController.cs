using Microsoft.AspNetCore.Mvc;
using MyDotNetApp.Services;
using MyDotNetApp.Data.Concrete;
using MyDotNetApp.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MyDotNetApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OutboxController : ControllerBase
{
    private readonly OutboxProcessorServiceScaled _outboxProcessor;
    private readonly IOutboxReadRepository _readRepo;
    private readonly IOutboxWriteRepository _writeRepo;
    private readonly OutboxProcessingService _outboxService;
    private readonly ILogger<OutboxController> _logger;

    public OutboxController(
        OutboxProcessorServiceScaled outboxProcessor,
        IOutboxReadRepository readRepo,
        IOutboxWriteRepository writeRepo,
        OutboxProcessingService outboxService,
        ILogger<OutboxController> logger)
    {
        _outboxProcessor = outboxProcessor;
        _readRepo = readRepo;
        _writeRepo = writeRepo;
        _outboxService = outboxService;
        _logger = logger;
    }

    /// <summary>
    /// Manually trigger outbox processing - directly polls and adds messages to processing queue
    /// Returns count of messages added
    /// </summary>
    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerProcessing()
    {
        _logger.LogInformation("Received manual trigger request from API");
        var cancellationToken = HttpContext?.RequestAborted ?? default;
        var count = await _outboxProcessor.TriggerPollAsync(cancellationToken);
        
        return Ok(new 
        { 
            success = true,
            messagesAdded = count,
            message = count > 0 ? $"Processing {count} messages" : "No messages to process",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get current outbox processor statistics
    /// </summary>
    /// <returns>Processing statistics</returns>
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var stats = _outboxProcessor.GetProcessingStats();
        return Ok(stats);
    }

    /// <summary>
    /// Get pending (unpublished) messages
    /// </summary>
    /// <param name="batchSize">Number of messages to retrieve (default: 100)</param>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingMessages(int batchSize = 100)
    {
        _logger.LogInformation($"Fetching pending messages, batch size: {batchSize}");
        var messages = await _readRepo.GetPendingMessagesAsync(batchSize);
        return Ok(new { count = messages.Count(), messages });
    }

    /// <summary>
    /// Get published messages within a date range
    /// </summary>
    [HttpGet("published")]
    public async Task<IActionResult> GetPublishedMessages(DateTime? from = null, DateTime? to = null)
    {
        from ??= DateTime.UtcNow.AddDays(-7);
        to ??= DateTime.UtcNow;

        _logger.LogInformation($"Fetching published messages from {from} to {to}");
        var messages = await _readRepo.GetPublishedMessagesAsync(from.Value, to.Value);
        return Ok(new { count = messages.Count(), messages });
    }

    /// <summary>
    /// Create a new outbox message
    /// </summary>
    [HttpPost("messages")]
    public async Task<IActionResult> CreateMessage([FromBody] CreateOutboxMessageRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var message = new Models.OutboxMessage
            {
                Topic = request.Topic,
                Payload = request.Payload,
                IsPublished = false,
                CreatedAt = DateTime.UtcNow
            };

            var id = await _outboxService.InsertMessageAsync(message);
            _logger.LogInformation($"Created outbox message {id} for topic {request.Topic}");

            return CreatedAtAction(nameof(GetMessageById), new { id }, new { messageId = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating outbox message");
            return StatusCode(500, new { error = "Failed to create message", details = ex.Message });
        }
    }

    /// <summary>
    /// Get message by ID
    /// </summary>
    [HttpGet("messages/{id}")]
    public async Task<IActionResult> GetMessageById(int id)
    {
        var message = await _readRepo.GetByKeyAsync(id);
        if (message == null)
            return NotFound(new { message = $"Message {id} not found" });

        return Ok(message);
    }

    /// <summary>
    /// Process pending messages
    /// </summary>
    [HttpPost("process")]
    public async Task<IActionResult> ProcessMessages(int batchSize = 100)
    {
        try
        {
            _logger.LogInformation($"Starting manual message processing with batch size: {batchSize}");
            await _outboxService.ProcessPendingMessagesAsync(batchSize);
            return Ok(new { success = true, message = "Messages processed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing messages");
            return StatusCode(500, new { error = "Failed to process messages", details = ex.Message });
        }
    }
}

public record CreateOutboxMessageRequest(string Topic, string Payload);
