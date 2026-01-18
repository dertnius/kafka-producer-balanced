using Microsoft.AspNetCore.Mvc;
using MyDotNetApp.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace MyDotNetApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OutboxController : ControllerBase
{
    private readonly OutboxProcessorServiceScaled _outboxProcessor;
    private readonly ILogger<OutboxController> _logger;

    public OutboxController(OutboxProcessorServiceScaled outboxProcessor, ILogger<OutboxController> logger)
    {
        _outboxProcessor = outboxProcessor;
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
}
