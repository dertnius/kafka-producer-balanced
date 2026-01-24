using System.Diagnostics;

namespace MyDotNetApp.Diagnostics;

public static class Telemetry
{
    public const string ServiceName = "mydotnetapp";
    
    public static ActivitySource ActivitySource { get; } = new ActivitySource(ServiceName);
}
