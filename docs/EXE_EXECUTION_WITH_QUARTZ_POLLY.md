# Executing Windows EXE with Quartz.NET and Polly

Complete implementation guide for executing a Windows executable from a Web API with retry logic and Event Log monitoring.

## Overview

This solution handles executables that:
- Don't use stdout/stderr
- Log errors to Windows Event Log
- May fail silently
- Need retry logic with exponential backoff

**Tech Stack:**
- **Quartz.NET**: Job scheduling and management
- **Polly**: Retry policies and resilience
- **ASP.NET Core**: Web API endpoints
- **Event Log Monitoring**: Failure detection

---

## 1. Package Installation

```bash
dotnet add package Quartz
dotnet add package Quartz.Extensions.Hosting
dotnet add package Polly
```

---

## 2. Job Implementation with Polly Retry

Create `Jobs/ExeExecutionJob.cs`:

```csharp
using Quartz;
using Polly;
using System.Diagnostics;

namespace YourNamespace.Jobs;

public class ExeExecutionJob : IJob
{
    private readonly ILogger<ExeExecutionJob> _logger;
    private readonly IAsyncPolicy _retryPolicy;

    public ExeExecutionJob(ILogger<ExeExecutionJob> logger)
    {
        _logger = logger;
        
        // Polly retry policy with exponential backoff
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} after {Delay}s due to: {Error}", 
                        retryCount, timeSpan.TotalSeconds, exception.Message);
                });
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var jobId = context.FireInstanceId;
        var dataMap = context.MergedJobDataMap;
        
        var exePath = dataMap.GetString("ExePath") ?? throw new ArgumentException("ExePath is required");
        var arguments = dataMap.GetString("Arguments") ?? "";
        var eventLogSource = dataMap.GetString("EventLogSource") ?? "Application";

        _logger.LogInformation("Starting job {JobId} - Executing {ExePath}", jobId, exePath);

        try
        {
            var success = await _retryPolicy.ExecuteAsync(async () =>
            {
                return await ExecuteExeAndVerify(exePath, arguments, eventLogSource);
            });

            context.Result = new { success, jobId, completedAt = DateTime.UtcNow };
            _logger.LogInformation("Job {JobId} completed successfully", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed after all retries", jobId);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }

    private async Task<bool> ExecuteExeAndVerify(string exePath, string arguments, string eventLogSource)
    {
        var startTime = DateTime.UtcNow;

        // Execute the process
        var processInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = false,
            RedirectStandardOutput = false
        };

        using var process = Process.Start(processInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start process");

        await process.WaitForExitAsync();
        
        // Wait a bit for Event Log to write
        await Task.Delay(1000);

        // Check Event Log for errors
        if (HasEventLogErrors(startTime, eventLogSource, exePath))
        {
            throw new InvalidOperationException($"Process execution failed - error found in Event Log");
        }

        return true;
    }

    private bool HasEventLogErrors(DateTime sinceTime, string logSource, string exePath)
    {
        try
        {
            var exeName = Path.GetFileNameWithoutExtension(exePath);
            using var eventLog = new EventLog(logSource);
            
            var errors = eventLog.Entries
                .Cast<EventLogEntry>()
                .Where(e => e.TimeGenerated >= sinceTime.AddSeconds(-5) && 
                           e.EntryType == EventLogEntryType.Error &&
                           (e.Source.Contains(exeName, StringComparison.OrdinalIgnoreCase) ||
                            e.Message.Contains(exeName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (errors.Any())
            {
                _logger.LogError("Event Log errors found: {Errors}", 
                    string.Join("; ", errors.Select(e => e.Message)));
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to check Event Log");
            return false; // Don't fail if can't check event log
        }
    }
}
```

---

## 3. Configure Quartz in Program.cs

```csharp
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// Add Quartz services
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
    
    // Configure default settings
    q.UseSimpleTypeLoader();
    q.UseInMemoryStore();
    q.UseDefaultThreadPool(tp =>
    {
        tp.MaxConcurrency = 10; // Max 10 concurrent jobs
    });
});

// Add Quartz hosted service
builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.Run();
```

---

## 4. API Controller

Create `Controllers/ProcessController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Quartz;

namespace YourNamespace.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProcessController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<ProcessController> _logger;

    public ProcessController(ISchedulerFactory schedulerFactory, ILogger<ProcessController> logger)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Execute a process immediately
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteProcess([FromBody] ExecuteRequest request)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        
        // Create job with parameters
        var job = JobBuilder.Create<ExeExecutionJob>()
            .WithIdentity($"exe-job-{Guid.NewGuid()}")
            .UsingJobData("ExePath", request.ExePath)
            .UsingJobData("Arguments", request.Arguments ?? "")
            .UsingJobData("EventLogSource", request.EventLogSource ?? "Application")
            .Build();

        // Trigger immediately
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"exe-trigger-{job.Key.Name}")
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        _logger.LogInformation("Scheduled job {JobKey}", job.Key);

        return Accepted(new 
        { 
            jobId = job.Key.Name,
            statusUrl = $"/api/process/status/{job.Key.Name}",
            message = "Job scheduled for execution"
        });
    }

    /// <summary>
    /// Schedule a process to run after a delay
    /// </summary>
    [HttpPost("schedule")]
    public async Task<IActionResult> ScheduleProcess([FromBody] ScheduleRequest request)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        
        var job = JobBuilder.Create<ExeExecutionJob>()
            .WithIdentity($"exe-job-{Guid.NewGuid()}")
            .UsingJobData("ExePath", request.ExePath)
            .UsingJobData("Arguments", request.Arguments ?? "")
            .UsingJobData("EventLogSource", request.EventLogSource ?? "Application")
            .Build();

        // Schedule for later
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"exe-trigger-{job.Key.Name}")
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(request.DelaySeconds))
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        return Accepted(new 
        { 
            jobId = job.Key.Name,
            scheduledFor = trigger.StartTimeUtc,
            message = $"Job scheduled to run in {request.DelaySeconds} seconds"
        });
    }

    /// <summary>
    /// Create a recurring job with cron schedule
    /// </summary>
    [HttpPost("recurring")]
    public async Task<IActionResult> CreateRecurringJob([FromBody] RecurringRequest request)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        
        var job = JobBuilder.Create<ExeExecutionJob>()
            .WithIdentity(request.JobName)
            .UsingJobData("ExePath", request.ExePath)
            .UsingJobData("Arguments", request.Arguments ?? "")
            .StoreDurably() // Keep job definition even when not scheduled
            .Build();

        // Cron trigger (e.g., "0 0/5 * * * ?" = every 5 minutes)
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{request.JobName}-trigger")
            .WithCronSchedule(request.CronExpression)
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        return Ok(new 
        { 
            jobName = request.JobName,
            cronExpression = request.CronExpression,
            nextRunTime = trigger.GetNextFireTimeUtc(),
            message = "Recurring job created"
        });
    }

    /// <summary>
    /// Get status of a specific job
    /// </summary>
    [HttpGet("status/{jobId}")]
    public async Task<IActionResult> GetJobStatus(string jobId)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(jobId);

        var jobDetail = await scheduler.GetJobDetail(jobKey);
        if (jobDetail == null)
            return NotFound(new { error = "Job not found" });

        var triggers = await scheduler.GetTriggersOfJob(jobKey);
        var trigger = triggers.FirstOrDefault();

        var state = trigger != null 
            ? await scheduler.GetTriggerState(trigger.Key)
            : TriggerState.None;

        return Ok(new
        {
            jobId,
            state = state.ToString(),
            nextFireTime = trigger?.GetNextFireTimeUtc(),
            previousFireTime = trigger?.GetPreviousFireTimeUtc()
        });
    }

    /// <summary>
    /// List all jobs
    /// </summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> GetAllJobs()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobGroups = await scheduler.GetJobGroupNames();
        
        var jobs = new List<object>();
        foreach (var group in jobGroups)
        {
            var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group));
            foreach (var jobKey in jobKeys)
            {
                var detail = await scheduler.GetJobDetail(jobKey);
                var triggers = await scheduler.GetTriggersOfJob(jobKey);
                
                jobs.Add(new
                {
                    jobId = jobKey.Name,
                    group = jobKey.Group,
                    exePath = detail?.JobDataMap.GetString("ExePath"),
                    triggers = triggers.Select(t => new
                    {
                        state = scheduler.GetTriggerState(t.Key).Result.ToString(),
                        nextFire = t.GetNextFireTimeUtc(),
                        previousFire = t.GetPreviousFireTimeUtc()
                    })
                });
            }
        }

        return Ok(jobs);
    }

    /// <summary>
    /// Delete a job
    /// </summary>
    [HttpDelete("jobs/{jobId}")]
    public async Task<IActionResult> DeleteJob(string jobId)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var deleted = await scheduler.DeleteJob(new JobKey(jobId));

        return deleted 
            ? Ok(new { message = "Job deleted" })
            : NotFound(new { error = "Job not found" });
    }
}

// Request models
public record ExecuteRequest(string ExePath, string? Arguments, string? EventLogSource);
public record ScheduleRequest(string ExePath, string? Arguments, int DelaySeconds, string? EventLogSource);
public record RecurringRequest(string JobName, string ExePath, string CronExpression, string? Arguments, string? EventLogSource);
```

---

## 5. Enhanced Job with Result Storage (Optional)

If you need to persist job results, add this enhanced version:

```csharp
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

public class ExeExecutionJobWithStorage : IJob
{
    private readonly ILogger<ExeExecutionJobWithStorage> _logger;
    private readonly IDistributedCache _cache;
    private readonly IAsyncPolicy _retryPolicy;

    public ExeExecutionJobWithStorage(
        ILogger<ExeExecutionJobWithStorage> logger,
        IDistributedCache cache)
    {
        _logger = logger;
        _cache = cache;
        
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} after {Delay}s", retryCount, timeSpan.TotalSeconds);
                });
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var jobId = context.FireInstanceId;
        var dataMap = context.MergedJobDataMap;
        
        try
        {
            // Store status: processing
            await UpdateJobStatus(jobId, "processing", null);

            // Execute with retries
            var exePath = dataMap.GetString("ExePath")!;
            var arguments = dataMap.GetString("Arguments") ?? "";
            var eventLogSource = dataMap.GetString("EventLogSource") ?? "Application";

            var success = await _retryPolicy.ExecuteAsync(async () =>
            {
                return await ExecuteExeAndVerify(exePath, arguments, eventLogSource);
            });

            // Store status: completed
            await UpdateJobStatus(jobId, "completed", new { success });
            
            context.Result = new { success, jobId, completedAt = DateTime.UtcNow };
        }
        catch (Exception ex)
        {
            await UpdateJobStatus(jobId, "failed", new { error = ex.Message });
            _logger.LogError(ex, "Job {JobId} failed", jobId);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }

    private async Task UpdateJobStatus(string jobId, string status, object? data)
    {
        var statusData = new
        {
            jobId,
            status,
            timestamp = DateTime.UtcNow,
            data
        };

        await _cache.SetStringAsync(
            $"job-status:{jobId}",
            JsonSerializer.Serialize(statusData),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
            });
    }

    private async Task<bool> ExecuteExeAndVerify(string exePath, string arguments, string eventLogSource)
    {
        // Same implementation as ExeExecutionJob
        throw new NotImplementedException();
    }
}
```

Then register distributed cache in Program.cs:

```csharp
builder.Services.AddDistributedMemoryCache(); // Or AddStackExchangeRedisCache for production
```

---

## 6. Polly Configuration Options

### Option A: Exponential Backoff (Recommended)
```csharp
Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(
        retryCount: 5,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
// Delays: 2s, 4s, 8s, 16s, 32s
```

### Option B: Fixed Delay with Jitter
```csharp
Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(
        retryCount: 5,
        sleepDurationProvider: attempt => 
            TimeSpan.FromSeconds(3) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)));
// Delays: ~3s each (with randomness to prevent thundering herd)
```

### Option C: Exponential with Max Wait
```csharp
Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(
        retryCount: 5,
        sleepDurationProvider: attempt => 
            TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 30)));
// Delays: 2s, 4s, 8s, 16s, 30s (capped at 30s)
```

### Option D: Circuit Breaker + Retry
```csharp
var retryPolicy = Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

var circuitBreakerPolicy = Policy
    .Handle<Exception>()
    .CircuitBreakerAsync(
        exceptionsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromMinutes(1));

var combinedPolicy = Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
```

---

## 7. Common Cron Expressions

```csharp
// Every 5 minutes
"0 0/5 * * * ?"

// Every hour at minute 0
"0 0 * * * ?"

// Every day at 2 AM
"0 0 2 * * ?"

// Every weekday at 9 AM
"0 0 9 ? * MON-FRI"

// Every 30 seconds
"0/30 * * * * ?"

// Every Monday at 8:30 AM
"0 30 8 ? * MON"

// First day of every month at midnight
"0 0 0 1 * ?"

// Every 15 minutes between 9 AM and 5 PM on weekdays
"0 0/15 9-17 ? * MON-FRI"
```

### Cron Format
```
┌─────────── second (0 - 59)
│ ┌───────── minute (0 - 59)
│ │ ┌─────── hour (0 - 23)
│ │ │ ┌───── day of month (1 - 31)
│ │ │ │ ┌─── month (1 - 12)
│ │ │ │ │ ┌─ day of week (0 - 6) (Sunday=0)
│ │ │ │ │ │
│ │ │ │ │ │
* * * * * *
```

---

## 8. API Usage Examples

### Execute Immediately
```bash
curl -X POST http://localhost:5000/api/process/execute \
  -H "Content-Type: application/json" \
  -d '{
    "exePath": "C:\\Tools\\MyApp.exe",
    "arguments": "--mode production",
    "eventLogSource": "Application"
  }'
```

Response:
```json
{
  "jobId": "exe-job-abc123",
  "statusUrl": "/api/process/status/exe-job-abc123",
  "message": "Job scheduled for execution"
}
```

### Schedule for Later
```bash
curl -X POST http://localhost:5000/api/process/schedule \
  -H "Content-Type: application/json" \
  -d '{
    "exePath": "C:\\Tools\\MyApp.exe",
    "arguments": "--mode production",
    "delaySeconds": 300
  }'
```

### Create Recurring Job
```bash
curl -X POST http://localhost:5000/api/process/recurring \
  -H "Content-Type: application/json" \
  -d '{
    "jobName": "daily-backup",
    "exePath": "C:\\Tools\\Backup.exe",
    "cronExpression": "0 0 2 * * ?",
    "arguments": "--full"
  }'
```

### Check Job Status
```bash
curl http://localhost:5000/api/process/status/exe-job-abc123
```

Response:
```json
{
  "jobId": "exe-job-abc123",
  "state": "Complete",
  "nextFireTime": null,
  "previousFireTime": "2026-01-25T10:30:00Z"
}
```

### List All Jobs
```bash
curl http://localhost:5000/api/process/jobs
```

### Delete Job
```bash
curl -X DELETE http://localhost:5000/api/process/jobs/daily-backup
```

---

## 9. Event Log Monitoring Details

The job monitors Windows Event Log for errors. Customize the filtering logic:

```csharp
private bool HasEventLogErrors(DateTime sinceTime, string logSource, string exePath)
{
    try
    {
        var exeName = Path.GetFileNameWithoutExtension(exePath);
        using var eventLog = new EventLog(logSource);
        
        // Adjust time window (checking 5 seconds before process start)
        var checkFrom = sinceTime.AddSeconds(-5);
        
        var errors = eventLog.Entries
            .Cast<EventLogEntry>()
            .Where(e => 
                e.TimeGenerated >= checkFrom && 
                e.EntryType == EventLogEntryType.Error &&
                // Filter by source or message content
                (e.Source.Contains(exeName, StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains(exeName, StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains("specific error keyword")))
            .ToList();

        if (errors.Any())
        {
            _logger.LogError("Event Log errors found: {Errors}", 
                string.Join("; ", errors.Select(e => $"[{e.Source}] {e.Message}")));
            return true;
        }

        return false;
    }
    catch (SecurityException ex)
    {
        _logger.LogWarning(ex, "No permission to access Event Log");
        return false;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Unable to check Event Log");
        return false;
    }
}
```

### Event Log Sources
Common Event Log sources to monitor:
- `Application` - General application events
- `System` - System-level events
- Custom source name if your exe writes to a specific source

---

## 10. Advanced Configuration

### Persistent Job Store (SQL Server)

For production, use persistent storage:

```csharp
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
    
    // Use SQL Server for persistence
    q.UsePersistentStore(store =>
    {
        store.UseSqlServer(builder.Configuration.GetConnectionString("QuartzDb"));
        store.UseJsonSerializer();
    });
    
    q.UseDefaultThreadPool(tp =>
    {
        tp.MaxConcurrency = 10;
    });
});
```

### Job Listener for Monitoring

```csharp
public class JobExecutionListener : IJobListener
{
    private readonly ILogger<JobExecutionListener> _logger;

    public string Name => "JobExecutionListener";

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken ct)
    {
        _logger.LogInformation("Job {JobKey} about to execute", context.JobDetail.Key);
    }

    public async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? ex, CancellationToken ct)
    {
        if (ex != null)
        {
            _logger.LogError(ex, "Job {JobKey} failed", context.JobDetail.Key);
        }
        else
        {
            _logger.LogInformation("Job {JobKey} completed successfully", context.JobDetail.Key);
        }
    }

    public async Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken ct)
    {
        _logger.LogWarning("Job {JobKey} was vetoed", context.JobDetail.Key);
    }
}

// Register in Program.cs
builder.Services.AddQuartz(q =>
{
    // ... other config
    q.AddJobListener<JobExecutionListener>();
});
```

---

## 11. Testing

### Unit Test Example

```csharp
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;

public class ExeExecutionJobTests
{
    [Fact]
    public async Task Execute_WithValidExe_Succeeds()
    {
        // Arrange
        var logger = new Mock<ILogger<ExeExecutionJob>>();
        var job = new ExeExecutionJob(logger.Object);
        
        var context = new Mock<IJobExecutionContext>();
        var dataMap = new JobDataMap
        {
            { "ExePath", "C:\\Windows\\System32\\cmd.exe" },
            { "Arguments", "/c echo test" },
            { "EventLogSource", "Application" }
        };
        
        context.Setup(c => c.MergedJobDataMap).Returns(dataMap);
        context.Setup(c => c.FireInstanceId).Returns(Guid.NewGuid().ToString());
        
        // Act
        await job.Execute(context.Object);
        
        // Assert
        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("completed successfully")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
```

---

## 12. Benefits Summary

✅ **Non-blocking**: API responds immediately  
✅ **Resilient**: Automatic retries with exponential backoff  
✅ **Observable**: Full job status tracking and logging  
✅ **Scalable**: Background processing doesn't block HTTP threads  
✅ **Flexible**: Support for immediate, scheduled, and recurring execution  
✅ **Production-ready**: Quartz.NET is battle-tested  
✅ **Event Log integration**: Detects silent failures  
✅ **Cancellation support**: Graceful shutdown  

---

## 13. Deployment Considerations

### Windows Service Permissions
Ensure your application has permission to:
- Execute the target exe
- Read Windows Event Log
- Write to configured log sources

### appsettings.json Configuration

```json
{
  "Quartz": {
    "MaxConcurrency": 10,
    "RetryCount": 5,
    "RetryDelaySeconds": 2
  },
  "ExeExecution": {
    "DefaultEventLogSource": "Application",
    "EventLogCheckDelayMs": 1000,
    "ProcessTimeoutSeconds": 30
  }
}
```

### Health Checks

Add health check for Quartz:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<QuartzHealthCheck>("quartz");

public class QuartzHealthCheck : IHealthCheck
{
    private readonly ISchedulerFactory _schedulerFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
    {
        var scheduler = await _schedulerFactory.GetScheduler(ct);
        var isStarted = scheduler.IsStarted;
        
        return isStarted 
            ? HealthCheckResult.Healthy("Quartz scheduler is running")
            : HealthCheckResult.Unhealthy("Quartz scheduler is not running");
    }
}
```

---

## 14. Troubleshooting

### Job Not Executing
- Check logs for exceptions
- Verify exe path is valid
- Ensure Quartz hosted service is registered
- Check thread pool settings

### Event Log Access Denied
```csharp
// Run application with elevated privileges or use try-catch
catch (SecurityException ex)
{
    _logger.LogWarning("No Event Log access - skipping verification");
    return true; // Assume success if can't verify
}
```

### Too Many Retries
- Adjust `retryCount` in Polly policy
- Add circuit breaker to prevent excessive retries
- Monitor and alert on retry patterns

---

## Resources

- [Quartz.NET Documentation](https://www.quartz-scheduler.net/)
- [Polly Documentation](https://github.com/App-vNext/Polly)
- [Cron Expression Generator](https://www.freeformatter.com/cron-expression-generator-quartz.html)
- [Windows Event Log API](https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.eventlog)

---

## Complete Implementation Checklist

- [ ] Install NuGet packages (Quartz, Polly)
- [ ] Create `ExeExecutionJob.cs`
- [ ] Configure Quartz in `Program.cs`
- [ ] Create `ProcessController.cs`
- [ ] Add request models
- [ ] Configure logging
- [ ] Test immediate execution
- [ ] Test scheduled execution
- [ ] Test recurring jobs
- [ ] Implement Event Log monitoring
- [ ] Add error handling
- [ ] Configure retry policies
- [ ] Add health checks
- [ ] Deploy and monitor

---

**Last Updated**: January 25, 2026
