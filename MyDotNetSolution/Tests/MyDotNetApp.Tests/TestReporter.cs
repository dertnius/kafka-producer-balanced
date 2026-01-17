using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MyDotNetApp.Tests
{
    /// <summary>
    /// Utility for generating test result reports
    /// </summary>
    public static class TestReporter
    {
        public static string GenerateReport(string testName, ProcessingResult result)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("");
            sb.AppendLine("╔════════════════════════════════════════════════════════════════╗");
            sb.AppendLine($"║ TEST RESULTS: {testName.PadRight(45)} ║");
            sb.AppendLine("╠════════════════════════════════════════════════════════════════╣");
            
            // Core metrics
            sb.AppendLine($"║ Messages Processed:      {result.ProcessedCount:N0}".PadRight(65) + "║");
            sb.AppendLine($"║ Messages Failed:         {result.FailedCount:N0}".PadRight(65) + "║");
            sb.AppendLine($"║ Elapsed Time:            {result.ElapsedMs}ms".PadRight(65) + "║");
            
            if (result.ElapsedMs > 0)
            {
                var throughput = result.ProcessedCount / (result.ElapsedMs / 1000d);
                var avgTimePerMsg = (result.ElapsedMs / (double)result.ProcessedCount * 1000);
                sb.AppendLine($"║ Throughput:              {throughput:N0} msg/sec".PadRight(65) + "║");
                sb.AppendLine($"║ Avg Time per Message:    {avgTimePerMsg:N3}µs".PadRight(65) + "║");
            }
            
            sb.AppendLine("╠════════════════════════════════════════════════════════════════╣");
            
            // Distribution metrics
            if (result.ProcessedByStid.Keys.Count > 0)
            {
                sb.AppendLine($"║ Unique STIDs:            {result.ProcessedByStid.Keys.Count}".PadRight(65) + "║");
                if (result.ProcessedCount > 0)
                {
                    var avgPerStid = result.ProcessedCount / (double)result.ProcessedByStid.Keys.Count;
                    sb.AppendLine($"║ Avg Messages per STID:   {avgPerStid:N0}".PadRight(65) + "║");
                }
            }
            
            if (result.ProcessedPoolCodes.Count > 0)
            {
                var uniquePoolCodes = result.ProcessedPoolCodes.Distinct().Count();
                sb.AppendLine($"║ Unique Pool Codes:       {uniquePoolCodes}".PadRight(65) + "║");
                if (result.ProcessedCount > 0)
                {
                    var avgPerPool = result.ProcessedCount / (double)uniquePoolCodes;
                    sb.AppendLine($"║ Avg Messages per Pool:   {avgPerPool:N0}".PadRight(65) + "║");
                }
            }
            
            sb.AppendLine("╚════════════════════════════════════════════════════════════════╝");
            
            return sb.ToString();
        }
    }
}
