# Production Deployment Checklist

## Pre-Deployment Review

### Code Quality
- [ ] All compilation errors resolved
- [ ] No warnings in Release build
- [ ] Code reviewed by team member
- [ ] Unit tests passing
- [ ] Integration tests passing

### Security
- [ ] Connection strings use environment variables (not hardcoded)
- [ ] All SQL queries are parameterized (no string interpolation)
- [ ] HTTPS enabled in production
- [ ] API endpoints have proper authentication/authorization
- [ ] Sensitive data not logged
- [ ] No hardcoded API keys or credentials

### Configuration
- [ ] appsettings.Production.json configured correctly
- [ ] Database connection string verified
- [ ] Kafka bootstrap servers configured
- [ ] Logging levels set appropriately (Warning/Error for production)
- [ ] Batch sizes tuned for expected load
- [ ] Retry limits configured

### Database
- [ ] Outbox table created with proper schema
- [ ] Indexes created for performance
- [ ] Database backups configured
- [ ] Database restore plan documented
- [ ] Connection pool settings optimized

### Kafka
- [ ] Kafka cluster running and verified
- [ ] Topic created with appropriate partitions
- [ ] Consumer groups configured
- [ ] Retention policy set
- [ ] Monitoring/alerting configured

### Infrastructure
- [ ] Application server capacity planned
- [ ] Memory requirements validated
- [ ] CPU requirements validated
- [ ] Network connectivity verified
- [ ] Firewall rules configured for database/Kafka access
- [ ] DNS resolution working

### Deployment
- [ ] Build artifacts ready
- [ ] Deployment script tested
- [ ] Rollback procedure documented
- [ ] Service startup verified
- [ ] Health check endpoint working
- [ ] Application restarts properly

### Monitoring & Logging
- [ ] Structured logging configured
- [ ] Log aggregation service connected (if applicable)
- [ ] Alerting rules created for errors
- [ ] Performance metrics being captured
- [ ] Dashboard created for monitoring

### Documentation
- [ ] Deployment guide completed
- [ ] Configuration guide documented
- [ ] Runbook created for common issues
- [ ] Emergency contact list available
- [ ] Disaster recovery plan documented

## Deployment Steps

1. **Prepare Environment**
   ```bash
   dotnet clean
   dotnet restore
   dotnet build -c Release
   ```

2. **Run Tests**
   ```bash
   dotnet test
   ```

3. **Create Release Build**
   ```bash
   dotnet publish -c Release -o ./publish
   ```

4. **Backup Current Version**
   ```bash
   # Backup existing application and database
   ```

5. **Deploy Application**
   ```bash
   # Stop current service
   # Copy new build to deployment location
   # Start service
   ```

6. **Verify Deployment**
   ```bash
   # Check health endpoint
   curl https://your-app/health
   
   # Monitor logs
   # Verify message processing
   ```

## Post-Deployment Verification

- [ ] Application started successfully
- [ ] No errors in logs (first 5 minutes)
- [ ] Database connection working
- [ ] Kafka producer working
- [ ] Messages being processed
- [ ] Health check passing
- [ ] Performance metrics normal
- [ ] No memory leaks detected

## Rollback Plan

If issues occur:

1. **Immediate Actions**
   - Stop the application
   - Restore previous version
   - Restart service
   - Monitor for stability

2. **Investigation**
   - Review logs of failed deployment
   - Check database consistency
   - Verify Kafka status
   - Document the issue

3. **Communication**
   - Notify relevant teams
   - Update stakeholders
   - Schedule post-mortem

## Monitoring After Deployment

- First 24 hours: Monitor every 1 hour
- First week: Monitor daily
- Ongoing: Monitor during business hours

Key metrics to watch:
- Error rate
- Message processing rate
- Database connection health
- Kafka producer latency
- Memory usage
- CPU usage
- Response time

## Performance Baseline

After stable deployment, establish baseline metrics:
- Messages processed per hour: ___________
- Average processing latency: ___________
- Peak CPU usage: ___________
- Peak memory usage: ___________
- Error rate: ___________

## Contacts

- **Application Owner**: ___________
- **Database Admin**: ___________
- **Infrastructure Team**: ___________
- **On-Call Support**: ___________

## Publishing Feature Deployment (NEW)

### Pre-Deployment
- [ ] Review PUBLISHING_IMPLEMENTATION.md
- [ ] Review PublishBatchHandler.cs
- [ ] Test batch handler enqueue/flush
- [ ] Backup Outbox table
- [ ] Review DATABASE_SCHEMA.sql migration scripts
- [ ] Establish performance baseline (current update latency)

### Schema Migration
- [ ] Apply migration scripts from DATABASE_SCHEMA.sql
- [ ] Verify new columns added (Publish, ProducedAt, ReceivedAt, etc.)
- [ ] Create indexes for performance
- [ ] Update table statistics

### Application Updates
- [ ] PublishBatchHandler service registered in DI
- [ ] PublishFlushBackgroundService registered
- [ ] Publishing configuration in appsettings
- [ ] Batch size and flush interval configured

### Post-Deployment Verification
- [ ] Check batch flush operations in logs
- [ ] Verify ProducedAt/ReceivedAt timestamps populated
- [ ] Measure throughput improvement (should be 10-50x)
- [ ] Monitor unpublished message count
- [ ] Verify batch operations complete in < 500ms
- [ ] Check database load reduction

---
**Updated**: January 17, 2026 - Added Publishing Feature Checks
