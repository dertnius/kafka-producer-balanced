# Quick Reference Guide

## Building & Running

```bash
# Build
dotnet build

# Run
dotnet run

# Build Release
dotnet build -c Release

# Publish Release
dotnet publish -c Release -o ./publish

# Run Tests
dotnet test
```

## Configuration

**Development** - `appsettings.json`
**Production** - `appsettings.Production.json` + Environment Variables

### Setting Environment
```bash
# Windows
set ASPNETCORE_ENVIRONMENT=Production

# Linux/Mac
export ASPNETCORE_ENVIRONMENT=Production
```

## Key Endpoints

- **Health Check**: `GET /health`
- **Default Port**: 5000 (HTTP), 5001 (HTTPS)

## Database

### Connection String Format
```
Server=hostname;Database=MyDotNetDb;User Id=sa;Password=YourPassword;Connection Timeout=30;
```

### Create Tables
```sql
CREATE TABLE Outbox (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    AggregateId NVARCHAR(255),
    EventType NVARCHAR(255),
    Payload NVARCHAR(MAX),
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

## Kafka Setup

### Required Settings
- `Kafka:BootstrapServers` - Kafka broker addresses (e.g., `localhost:9092`)
- `Kafka:TopicName` - Topic to publish to
- Database must have Outbox table

### Create Topic (if needed)
```bash
kafka-topics.sh --create --topic outbox-events --partitions 3 --replication-factor 1
```

## Logging

### Log Levels
- **Debug**: Detailed diagnostic information
- **Information**: General informational messages
- **Warning**: Warning messages for potential issues
- **Error**: Error messages for failures

### Configure in appsettings
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

## Performance Tuning

### Increase Throughput
```json
{
  "Processing": {
    "BatchSize": 5000,           // Increase batch size
    "MaxParallelStidGroups": 20, // Increase parallel workers
    "PollIntervalMs": 1000        // Reduce poll interval
  }
}
```

### Reduce Memory Usage
```json
{
  "Processing": {
    "BatchSize": 500,            // Decrease batch size
    "MaxParallelStidGroups": 5   // Reduce parallel workers
  }
}
```

## Monitoring

### Key Metrics to Watch
- Message processing rate
- Error rate
- Average latency
- Database connection pool usage
- Memory and CPU usage

### Health Check
```bash
curl http://localhost:5000/health
```

## Common Issues

### Messages Not Processing
1. Check if Kafka is running: `kafka-broker-api-versions.sh --bootstrap-server localhost:9092`
2. Verify topic exists: `kafka-topics.sh --list --bootstrap-server localhost:9092`
3. Check application logs for errors
4. Verify database connection

### High Memory Usage
1. Reduce `BatchSize` in settings
2. Reduce `MaxParallelStidGroups`
3. Check for memory leaks with profiler

### Kafka Connection Issues
1. Verify `BootstrapServers` setting
2. Check network connectivity to Kafka
3. Verify Kafka cluster is running
4. Check firewall rules

### Database Connection Issues
1. Verify connection string
2. Check SQL Server is running
3. Verify credentials
4. Check network connectivity

## Architecture Overview

```
Program.cs
    ↓
Startup.cs (DI Container Setup)
    ↓
MessaggingService (Background Service)
    ↓
[OutboxService] ← [IOutboxService]
[KafkaService]  ← [IKafkaService]
    ↓
Database (Outbox Table)
Kafka (Topic)
```

## Security Checklist

- ✅ Connection strings in environment variables
- ✅ HTTPS enabled in production
- ✅ No hardcoded credentials
- ✅ SQL parameterized queries
- ✅ Input validation on all inputs
- ✅ Error messages don't expose sensitive data
- ✅ Logging excludes sensitive data

## Deployment Checklist

- [ ] appsettings.Production.json configured
- [ ] Connection string verified
- [ ] Kafka brokers verified
- [ ] Database tables created
- [ ] Health endpoint tested
- [ ] Logs are being generated
- [ ] Performance meets expectations
- [ ] Monitoring is configured

## File Structure

```
MyDotNetSolution/
├── src/
│   └── MyDotNetApp/
│       ├── Controllers/         # API endpoints
│       │   └── kafkaproducer.cs
│       ├── Services/            # Business logic
│       │   ├── HomeBackgroundService.cs
│       │   ├── KafkaService.cs
│       │   └── OutboxService.cs
│       ├── Program.cs           # Entry point
│       ├── Startup.cs           # Configuration
│       ├── MyDotNetApp.csproj   # Project file
│       ├── appsettings.json     # Dev config
│       └── appsettings.Production.json
├── Tests/
│   └── HomeBackgroundServiceTests.cs
├── PRODUCTION_GUIDE.md
├── DEPLOYMENT_CHECKLIST.md
├── PRODUCTION_READINESS.md
└── README.md
```

## Resources

- [Microsoft Docs - Configuration](https://docs.microsoft.com/en-us/dotnet/core/extensions/configuration)
- [Kafka Documentation](https://kafka.apache.org/documentation/)
- [Dapper GitHub](https://github.com/DapperLib/Dapper)
- [Best Practices Guide](./PRODUCTION_GUIDE.md)

## Support

For issues or questions:
1. Check the [PRODUCTION_GUIDE.md](./PRODUCTION_GUIDE.md)
2. Review [DEPLOYMENT_CHECKLIST.md](./DEPLOYMENT_CHECKLIST.md)
3. Check application logs
4. Consult the development team

---

**Last Updated**: January 17, 2026  
**Status**: Production Ready
