using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MyDotNetApp.Diagnostics;
using System;
using System.Diagnostics;

namespace MyDotNetApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        using var activity = Telemetry.ActivitySource.StartActivity("HealthCheck");
        activity?.SetTag("endpoint", "health");
        activity?.SetTag("timestamp", DateTime.UtcNow.ToString("o"));
        
        _logger.LogInformation("Health check endpoint called");
        
        return Ok(new 
        { 
            status = "healthy",
            timestamp = DateTime.UtcNow,
            trace_id = Activity.Current?.TraceId.ToString()
        });
    }

    [HttpGet("trace-test")]
    public IActionResult TraceTest()
    {
        using var activity = Telemetry.ActivitySource.StartActivity("TraceTest");
        activity?.SetTag("test", "true");
        activity?.SetTag("custom.data", "This is a test trace");
        
        _logger.LogInformation("Trace test endpoint called - TraceId: {TraceId}", Activity.Current?.TraceId);
        
        // Simulate some work
        System.Threading.Thread.Sleep(100);
        
        return Ok(new 
        { 
            message = "Trace test completed",
            trace_id = Activity.Current?.TraceId.ToString(),
            span_id = Activity.Current?.SpanId.ToString()
        });
    }
}
