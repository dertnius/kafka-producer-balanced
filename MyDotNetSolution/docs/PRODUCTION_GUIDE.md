# MyDotNetApp - Production Ready

A high-performance .NET 5.0 application for processing outbox messages and producing them to Kafka with proper error handling, logging, and configuration management.

## Features

- ✅ **Event Outbox Pattern**: Reliable message delivery using the outbox pattern
- ✅ **Kafka Integration**: Publish events to Kafka with delivery confirmation
- ✅ **Async Processing**: Background service for continuous message processing
- ✅ **Error Handling**: Comprehensive exception handling and retry mechanisms
- ✅ **Structured Logging**: Detailed logging for debugging and monitoring
- ✅ **Configuration Management**: Environment-based configuration (Development/Production)
- ✅ **SQL Injection Protection**: Parameterized queries using Dapper
- ✅ **Cancellation Support**: Graceful shutdown with proper CancellationToken handling

## Architecture

### Core Components

1. **MessaggingService** - Background service that polls the outbox table and processes messages
2. **OutboxService** - Data access layer for outbox operations with error tracking
3. **KafkaService** - Kafka producer implementation for message publishing
4. **OutboxProcessorService** - Advanced processor with multiple concurrent producers

### Design Patterns

- **Outbox Pattern**: Ensures reliable message delivery between services
- **Dependency Injection**: All services are registered in DI container
- **Async/Await**: Non-blocking operations throughout
- **Structured Logging**: Context-rich logging for production monitoring

## Getting Started

### Prerequisites

- .NET 5.0 SDK or later
- SQL Server (local or remote)
- Kafka cluster (local or remote)
- Visual Studio 2019+ or VS Code

### Installation

1. Clone the repository
```bash
git clone <repository-url>
cd MyDotNetSolution
```

2. Restore NuGet packages
```bash
dotnet restore
```

3. Configure connection strings in `appsettings.json`
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyDotNetDb;Trusted_Connection=true;"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "TopicName": "outbox-events"
  }
}
```

4. Create the Outbox table in SQL Server
```sql
CREATE TABLE Outbox (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    AggregateId NVARCHAR(255) NOT NULL,
    EventType NVARCHAR(255) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    Processed BIT DEFAULT 0,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    ProcessedAt DATETIME2 NULL,
    Stid NVARCHAR(255),
    Code NVARCHAR(255),
    Rank INT,
    Retry INT DEFAULT 0,
    ErrorCode NVARCHAR(100)
);

CREATE INDEX IX_Outbox_Processed ON Outbox(Processed, CreatedAt);
CREATE INDEX IX_Outbox_Stid ON Outbox(Stid, Processed);
```

5. Build and run
```bash
dotnet build
dotnet run
```

## Configuration

### Development Settings (`appsettings.json`)

```json
{
  "Processing": {
    "BatchSize": 1000,
    "MaxParallelStidGroups": 10,
    "PollIntervalMs": 5000,
    "MaxRetries": 3
  }
}
```

### Production Settings (`appsettings.Production.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  },
  "Processing": {
    "BatchSize": 5000,
    "MaxParallelStidGroups": 20,
    "PollIntervalMs": 2000,
    "MaxRetries": 5
  }
}
```

## Health Check

The application exposes a health endpoint:

```bash
curl http://localhost:5000/health
```

Response:
```json
{
  "status": "healthy"
}
```

## Error Handling

The application implements comprehensive error handling:

- **Database Errors**: Connection failures are logged and retried
- **Kafka Producer Errors**: Messages are marked for retry
- **Processing Errors**: Failed messages are tracked with error codes
- **Retry Logic**: Messages retry up to configured limit before being marked as failed

## Monitoring

### Logging

All operations are logged with appropriate severity levels:

- **Information**: Service startup/shutdown, message processing milestones
- **Warning**: Retries, recoverable failures
- **Error**: Unhandled exceptions, permanent failures

### Metrics

Performance metrics are available through logs:
- Messages fetched, produced, and marked as processed
- Processing rates
- Error counts

## Development

### Running Tests

```bash
cd Tests
dotnet test
```

### Building Release

```bash
dotnet build -c Release
```

## Deployment

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS runtime
WORKDIR /app
COPY bin/Release/net5.0/publish .
EXPOSE 80
ENTRYPOINT ["dotnet", "MyDotNetApp.dll"]
```

### Kubernetes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mydotnetapp
spec:
  replicas: 3
  selector:
    matchLabels:
      app: mydotnetapp
  template:
    metadata:
      labels:
        app: mydotnetapp
    spec:
      containers:
      - name: mydotnetapp
        image: mydotnetapp:latest
        ports:
        - containerPort: 80
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
```

## Security Considerations

1. **SQL Injection Protection**: All database queries use parameterized statements via Dapper
2. **Environment Configuration**: Sensitive data (connection strings, passwords) via environment variables
3. **HTTPS**: Enabled in production via `app.UseHsts()`
4. **Logging**: Sensitive data is not logged
5. **Error Handling**: Generic error messages in production, detailed logs for troubleshooting

## Performance Optimization

- **Batch Processing**: Messages processed in configurable batches
- **Connection Pooling**: SQL connection pooling managed by the driver
- **Async Operations**: Non-blocking async/await throughout
- **Kafka Producer Pooling**: Multiple concurrent producers for higher throughput
- **Partitioning**: Messages partitioned by STID for ordered processing

## Troubleshooting

### Application Won't Start

1. Check connection string in `appsettings.json`
2. Verify SQL Server is running and accessible
3. Ensure database tables exist
4. Check logs for detailed error messages

### Messages Not Processing

1. Verify Kafka is running and accessible
2. Check batch size configuration
3. Monitor database for connection issues
4. Review application logs for errors

### High Memory Usage

1. Reduce `MaxProducerBuffer` in Kafka settings
2. Lower `MaxParallelStidGroups` setting
3. Decrease `BatchSize` for smaller batches

## License

MIT License - See LICENSE file for details

## Support

For issues or questions, please create an issue in the repository or contact the development team.
