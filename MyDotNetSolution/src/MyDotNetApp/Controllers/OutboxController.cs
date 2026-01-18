using Microsoft.AspNetCore.Mvc;
using MyDotNetApp.Services;
using Microsoft.Extensions.Logging;
using System;

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
    /// Manually trigger outbox processing immediately (bypasses timer)
    /// </summary>
    /// <returns>Success confirmation</returns>
    [HttpPost("trigger")]
    public IActionResult TriggerProcessing()
    {
        _logger.LogInformation("Received manual trigger request from API");
        _outboxProcessor.TriggerProcessing();
        
        return Ok(new 
        { 
            success = true, 
            message = "Outbox processing triggered successfully. Messages will be processed immediately.",
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
