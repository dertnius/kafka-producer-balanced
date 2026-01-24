# Aspire Integration Guide

## Overview
.NET Aspire orchestrates and monitors distributed applications with a centralized dashboard.

## Integration Steps

### 1. Install Aspire Workload
```powershell
dotnet workload install aspire
```

### 2. Create ServiceDefaults Project
```powershell
cd MyDotNetSolution
dotnet new aspire-servicedefaults -n ServiceDefaults
dotnet sln add ServiceDefaults\ServiceDefaults.csproj
```

**ServiceDefaults.csproj** - Provides shared configuration:
- OpenTelemetry tracing
- Health checks
- Service discovery

### 3. Create AppHost Project
```powershell
dotnet new aspire-apphost -n MyDotNetApp.AppHost
dotnet sln add MyDotNetApp.AppHost\MyDotNetApp.AppHost.csproj
```

**MyDotNetApp.AppHost.csproj** - References:
```xml
<ProjectReference Include="..\src\MyDotNetApp\MyDotNetApp.csproj" />
```

### 4. Configure Program.cs (AppHost)
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var solutionDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, ".."));

// Add main web application
var app = builder.AddProject<Projects.MyDotNetApp>("mydotnetapp")
    .WithExternalHttpEndpoints();

// Add test executable
builder.AddExecutable(
    "tests",
    "pwsh",
    solutionDir,
    new[] { "-NoLogo", "-NoProfile", "-Command", 
            "cd Tests\\MyDotNetApp.Tests; dotnet test --logger \"console;verbosity=detailed\"" }
);

builder.Build().Run();
```

### 5. Update MyDotNetApp.csproj
Add Aspire service defaults reference:
```xml
<ProjectReference Include="..\ServiceDefaults\ServiceDefaults.csproj" />
```

### 6. Configure Program.cs (Main App)
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();  // Add Aspire defaults

// ... other configuration
var app = builder.Build();
app.UseServiceDefaults();  // Apply defaults
```

### 7. Add Root Endpoint (Optional)
In `Startup.cs`:
```csharp
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapGet("/", () => Results.Ok(new 
    {
        message = "MyDotNetApp API is running",
        endpoints = new[] { "/api/health", "/api/outbox/stats", ... }
    }));
});
```

## Running Aspire

```powershell
cd MyDotNetSolution
dotnet run --project MyDotNetApp.AppHost\MyDotNetApp.AppHost.csproj
```

**Dashboard Access:**
- HTTPS: https://localhost:17164
- HTTP: http://localhost:17164 (if ASPIRE_DASHBOARD__USEHTTPS=false)

## Resources
- **mydotnetapp** - Main web application (port 5055)
- **tests** - Test execution executable (on-demand)

## Key Features Enabled
- ✅ Distributed tracing (OpenTelemetry)
- ✅ Health checks monitoring
- ✅ Centralized logging
- ✅ Environment variable management
- ✅ Resource lifecycle management
- ✅ Local development dashboard

## Project Structure
```
MyDotNetSolution/
├── MyDotNetApp.AppHost/         # Orchestration host
├── ServiceDefaults/              # Shared Aspire defaults
├── src/
│   └── MyDotNetApp/              # Web application
├── Tests/
│   └── MyDotNetApp.Tests/        # Test project
└── MyDotNetSolution.sln
```

## Files Modified
1. **MyDotNetApp.csproj** - Added ServiceDefaults reference
2. **Program.cs (MyDotNetApp)** - Added `builder.AddServiceDefaults()`
3. **Startup.cs** - Added root endpoint configuration
4. **MyDotNetApp.AppHost.csproj** - Added MyDotNetApp reference (auto-generated)

## Files Created
1. **MyDotNetApp.AppHost/** - Complete orchestration project
2. **ServiceDefaults/** - Shared defaults library

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Port already in use | Kill processes: `Get-Process dotnet \| Stop-Process -Force` |
| Build fails to copy exe | Ensure all dotnet processes are stopped |
| Dashboard won't load | Check firewall, verify https://localhost:17164 in browser |
| Tests not showing output | Verify PowerShell executable path in Program.cs |

## References
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Aspire 8.2.2](https://github.com/dotnet/aspire)
