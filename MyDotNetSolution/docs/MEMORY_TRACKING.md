# Memory Tracking Implementation

## Overview

The application now tracks and logs detailed memory information at three key points:
1. **Service Startup** - Initial memory state
2. **Periodic Reporting** (every 10 seconds) - Current memory usage during processing
3. **Service Shutdown** - Final memory state and memory statistics

## Memory Metrics Tracked

### At Startup
- **Start Memory**: Amount of memory allocated at service startup
- **Current Memory**: Current working set
- **Available Memory**: System memory available

### During Processing (Every 10 seconds)
- **Start Memory**: Initial memory when service started
- **Current Memory**: Memory being used right now
- **Used Memory**: Difference between current and starting memory
- **Peak Memory**: Maximum memory used since startup

### At Shutdown
- **Start Memory**: Initial memory state
- **Final Memory**: Memory at shutdown
- **Total Used**: Total memory consumed during service lifetime
- **Peak Used**: Maximum additional memory used above baseline

## Implementation Details

### Updated Files

#### 1. PerformanceMetrics.cs
Added memory tracking capabilities:
- `StartMemoryBytes` - Memory at service startup
- `CurrentMemoryBytes` - Current memory usage
- `PeakMemoryBytes` - Peak memory reached
- `MemoryUsedBytes` - Current usage above baseline
- `RecordStartMemory()` - Initialize memory tracking (calls GC.Collect)
- `UpdatePeakMemory()` - Update peak on each operation (atomic)
- `LogMemoryStartup()` - Log at service startup
- `LogMemoryShutdown()` - Log summary at shutdown
- `LogMetrics()` - Enhanced to include memory info every 10 seconds

#### 2. OutboxProcessorServiceScaled.cs
Integrated memory logging:
- `StartAsync()` - Calls `_metrics.LogMemoryStartup(_logger)` 
- `StopAsync()` - Calls `_metrics.LogMemoryShutdown(_logger)`
- `ReportMetricsAsync()` - Already calls `_metrics.LogMetrics()` every 10s

## Sample Log Output

### Startup Log
```
🚀 Service Startup - Memory Info: Start=125.45 MB | Current=128.32 MB | Available: ~8192.50 MB
```

### Periodic Report (Every 10 seconds)
```
Metrics - Fetched: 50000 (5000/s) | Produced: 45000 (4500/s) | Marked: 22500 | Failed: 500 | Elapsed: 10.5s | Memory: Start=125.45 MB | Current=156.78 MB | Used=31.33 MB | Peak=167.45 MB
```

### Shutdown Log
```
🛑 Service Shutdown - Memory Summary: Start=125.45 MB | Final=128.92 MB | Total Used=3.47 MB | Peak Used=42.00 MB
```

## Memory Format

All memory values are automatically formatted to human-readable units:
- **Bytes (B)**: < 1 KB
- **Kilobytes (KB)**: 1 KB - 1 MB  
- **Megabytes (MB)**: 1 MB - 1 GB
- **Gigabytes (GB)**: ≥ 1 GB

Example: `156.78 MB`, `2.45 GB`, `512.34 KB`

## Memory Leak Prevention

The memory metrics reset every 10 seconds (see [MEMORY_LEAK_RESOLUTION.md](MEMORY_LEAK_RESOLUTION.md)):
- Counters (_fetched, _produced, _marked, _failed) are reset to zero
- Timer is restarted for the next measurement window
- **Peak memory is NOT reset** - continues to track highest usage across entire service lifetime

This design allows you to see:
- **Window metrics**: What happened in the last 10 seconds
- **Cumulative metrics**: Highest memory reached during entire runtime (in logs)

## How to Monitor Memory

### During Service Operation
1. Watch the logs every 10 seconds for memory updates
2. Look for the `Memory:` section in each metrics report
3. Check if `Used=` is growing (indicates memory leak)
4. Compare `Current` to `Start` to see actual memory footprint

### After Service Completes
1. Final shutdown log shows summary
2. Compare `Total Used` to see real memory consumption
3. Peak shows maximum memory during lifetime

## Memory Leak Detection

Watch for these warning signs:
- `Current` memory increasing rapidly over multiple 10-second windows
- `Used` (current - start) growing beyond expected application state
- `Peak` growing consistently instead of stabilizing

Example of healthy vs. problematic:
```
HEALTHY:
Window 1: Used=15 MB, Peak=30 MB
Window 2: Used=18 MB, Peak=31 MB  
Window 3: Used=16 MB, Peak=31 MB  ← Stabilized, memory reset between windows

PROBLEMATIC:
Window 1: Used=15 MB, Peak=30 MB
Window 2: Used=35 MB, Peak=45 MB  ← Growing
Window 3: Used=55 MB, Peak=65 MB  ← Continuing to grow - memory leak!
```

## Thread-Safe Memory Tracking

All memory updates use thread-safe atomic operations:
- `Interlocked.Exchange` for updating peak memory
- `Interlocked.Read` for reading current values
- Safe for concurrent access from multiple worker threads

## GC Integration

Memory tracking uses:
- `GC.GetTotalMemory(true)` at startup (full collection)
- `GC.GetTotalMemory(false)` during operation (no collection - faster)
- `GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, true)` at startup
- `GC.WaitForPendingFinalizers()` at startup to ensure clean baseline

## Configuration

The memory tracking is always active:
- No configuration needed
- Runs on every operation (`RecordFetched`, `RecordProduced`, etc.)
- Logged with standard application logger (controlled by Serilog settings)

## Dependencies

Built-in .NET capabilities - no additional dependencies:
- `System.Diagnostics.Process` (for memory info)
- `GC` class (garbage collection)
- `System.Threading.Interlocked` (atomic operations)
- Standard logging through ILogger

## Verification

To verify memory tracking is working:
1. Start the service
2. Watch for startup memory log with 🚀 emoji
3. After ~10 seconds, see memory metrics with `Memory:` section
4. Upon shutdown, see 🛑 memory summary
5. Check logs contain all three memory reports

## Future Enhancements

Potential improvements:
- Memory timeline graph (export to CSV)
- GC collection frequency tracking
- Object allocation rate monitoring
- Memory pressure alerts
- Integration with Application Insights or other APM tools

---

**Note**: Memory tracking is automatic and always active. No additional configuration or code changes needed to capture memory usage information in application logs.
