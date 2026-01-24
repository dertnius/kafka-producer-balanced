using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
        builder.AddServiceDefaults();
        
        // Configure services using Startup
        var startup = new Startup(builder.Configuration);
        startup.ConfigureServices(builder.Services);
        
        var app = builder.Build();
        
        // Configure middleware using Startup
        var logger = app.Services.GetRequiredService<ILogger<Startup>>();
        startup.Configure(app, app.Environment, logger);
        
        app.Run();
    }
}