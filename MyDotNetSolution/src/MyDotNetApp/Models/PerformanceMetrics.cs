using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;

namespace MyDotNetApp.Models;

/// <summary>
/// Performance tracking with memory monitoring
/// </summary>
public class PerformanceMetrics
{
    private long _fetched = 0;
    private long _produced = 0;
    private long _marked = 0;
    private long _failed = 0;
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private long _startMemoryBytes = 0;
    private long _peakMemoryBytes = 0;
    private long _lastReportedMemoryBytes = 0;

    public long FetchedCount => Interlocked.Read(ref _fetched);
    public long ProducedCount => Interlocked.Read(ref _produced);
    public long MarkedCount => Interlocked.Read(ref _marked);
    public long FailedCount => Interlocked.Read(ref _failed);
    public long StartMemoryBytes => Interlocked.Read(ref _startMemoryBytes);
    public long CurrentMemoryBytes => GC.GetTotalMemory(false);
    public long PeakMemoryBytes => Interlocked.Read(ref _peakMemoryBytes);
    public long MemoryUsedBytes => Math.Max(0, CurrentMemoryBytes - StartMemoryBytes);

    public PerformanceMetrics()
    {
        RecordStartMemory();
    }

    public void RecordStartMemory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, true);
        GC.WaitForPendingFinalizers();
        var startMem = GC.GetTotalMemory(true);
        Interlocked.Exchange(ref _startMemoryBytes, startMem);
        Interlocked.Exchange(ref _peakMemoryBytes, startMem);
    }

    private void UpdatePeakMemory()
    {
        var currentMem = GC.GetTotalMemory(false);
        long peakMem;
        do
        {
            peakMem = Interlocked.Read(ref _peakMemoryBytes);
        } while (currentMem > peakMem && 
                 !Interlocked.CompareExchange(ref _peakMemoryBytes, currentMem, peakMem).Equals(peakMem));
    }

    public void RecordFetched(int count)
    {
        Interlocked.Add(ref _fetched, count);
        UpdatePeakMemory();
    }

    public void RecordProduced()
    {
        Interlocked.Increment(ref _produced);
        UpdatePeakMemory();
    }

    public void RecordMarked(int count)
    {
        Interlocked.Add(ref _marked, count);
        UpdatePeakMemory();
    }

    public void RecordFailed()
    {
        Interlocked.Increment(ref _failed);
        UpdatePeakMemory();
    }

    /// <summary>
    /// Get public read-only access to counter values
    /// </summary>
    public long Fetched => Interlocked.Read(ref _fetched);
    public long Produced => Interlocked.Read(ref _produced);
    public long Marked => Interlocked.Read(ref _marked);
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>
    /// Format bytes to human-readable format (KB, MB, GB)
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        if (bytes >= GB)
            return $"{bytes / (double)GB:F2} GB";
        if (bytes >= MB)
            return $"{bytes / (double)MB:F2} MB";
        if (bytes >= KB)
            return $"{bytes / (double)KB:F2} KB";
        return $"{bytes} B";
    }

    public void LogMetrics(ILogger logger)
    {
        var elapsed = _timer.Elapsed.TotalSeconds;
        var fetchedRate = _fetched / elapsed;
        var producedRate = _produced / elapsed;
        var currentMem = CurrentMemoryBytes;
        var memoryUsed = MemoryUsedBytes;
        Interlocked.Exchange(ref _lastReportedMemoryBytes, currentMem);
        
        logger.LogInformation(
            "Metrics - Fetched: {Fetched} ({FetchedRate:F0}/s) | Produced: {Produced} ({ProducedRate:F0}/s) | Marked: {Marked} | Failed: {Failed} | Elapsed: {Elapsed:F1}s | Memory: Start={StartMemory} | Current={CurrentMemory} | Used={UsedMemory} | Peak={PeakMemory}",
            _fetched, fetchedRate, _produced, producedRate, _marked, _failed, elapsed,
            FormatBytes(_startMemoryBytes), FormatBytes(currentMem), FormatBytes(memoryUsed), FormatBytes(_peakMemoryBytes));
    }

    public void LogMemoryStartup(ILogger logger)
    {
        logger.LogInformation(
            "🚀 Service Startup - Memory Info: Start={StartMemory} | Current={CurrentMemory} | Available: ~{AvailableMemory}",
            FormatBytes(_startMemoryBytes), FormatBytes(CurrentMemoryBytes), 
            FormatBytes(GC.GetGCMemoryInfo().TotalCommittedBytes));
    }

    public void LogMemoryShutdown(ILogger logger)
    {
        var finalMem = CurrentMemoryBytes;
        var totalMemoryUsed = finalMem - _startMemoryBytes;
        logger.LogInformation(
            "🛑 Service Shutdown - Memory Summary: Start={StartMemory} | Final={FinalMemory} | Total Used={TotalUsed} | Peak Used={PeakUsed}",
            FormatBytes(_startMemoryBytes), FormatBytes(finalMem), FormatBytes(totalMemoryUsed), FormatBytes(PeakMemoryBytes - _startMemoryBytes));
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
