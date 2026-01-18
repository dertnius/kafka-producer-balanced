using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;

namespace MyDotNetApp.Models;

/// <summary>
/// Performance tracking
/// </summary>
public class PerformanceMetrics
{
    private long _fetched = 0;
    private long _produced = 0;
    private long _marked = 0;
    private long _failed = 0;
    private readonly Stopwatch _timer = Stopwatch.StartNew();

    public void RecordFetched(int count) => Interlocked.Add(ref _fetched, count);
    public void RecordProduced() => Interlocked.Increment(ref _produced);
    public void RecordMarked(int count) => Interlocked.Add(ref _marked, count);
    public void RecordFailed() => Interlocked.Increment(ref _failed);

    public void LogMetrics(ILogger logger)
    {
        var elapsed = _timer.Elapsed.TotalSeconds;
        var fetchedRate = _fetched / elapsed;
        var producedRate = _produced / elapsed;
        
        logger.LogInformation(
            "Metrics - Fetched: {Fetched} ({FetchedRate:F0}/s) | Produced: {Produced} ({ProducedRate:F0}/s) | Marked: {Marked} | Failed: {Failed} | Elapsed: {Elapsed:F1}s",
            _fetched, fetchedRate, _produced, producedRate, _marked, _failed, elapsed);
    }

    /// <summary>
    /// Reset all metrics counters and restart the timer to prevent memory accumulation
    /// Call after logging metrics to get periodic snapshots instead of cumulative totals
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _fetched, 0);
        Interlocked.Exchange(ref _produced, 0);
        Interlocked.Exchange(ref _marked, 0);
        Interlocked.Exchange(ref _failed, 0);
        _timer.Restart();
    }
}
