# Dapper Repository Pattern with Unit of Work

A complete implementation of the Repository Pattern with separate Read/Write repositories and Unit of Work for transaction management using Dapper ORM.

## Architecture Overview

```
┌─────────────────────────────────────────────────┐
│              Service Layer                       │
│  (OutboxProcessingService, etc.)                │
└────────────────┬────────────────────────────────┘
                 │
        ┌────────┴────────┐
        │                 │
┌───────▼──────┐   ┌──────▼────────┐
│  Read Repo   │   │  Write Repo   │
└───────┬──────┘   └──────┬────────┘
        │                 │
        └────────┬────────┘
                 │
        ┌────────▼────────┐
        │   Unit of Work  │
        │  (Transaction)  │
        └────────┬────────┘
                 │
        ┌────────▼────────┐
        │  DB Connection  │
        │   (Dapper)      │
        └─────────────────┘
```

## Key Components

### 1. Attributes
- `[Key]` - Mark properties as primary key (supports composite keys)
- `[Table("TableName")]` - Specify database table name

### 2. Repositories
- `IReadRepository<T>` / `ReadRepository<T>` - For queries (no transactions)
- `IWriteRepository<T>` / `WriteRepository<T>` - For insert/update/delete (with transactions)

### 3. Unit of Work
- `IUnitOfWork` / `UnitOfWork` - Manages transactions and connections

### 4. Helper
- `KeyHelper` - Reflects key information from entities

## Quick Start

### Step 1: Define Your Entity

```csharp
using MyDotNetApp.Data.Attributes;

[Table("OutboxMessage")]
public class OutboxMessage
{
    [Key]
    public int MessageId { get; set; }  // YOU define the key
    
    public string Topic { get; set; }
    public string Payload { get; set; }
    public bool IsPublished { get; set; }
}
```

### Step 2: Create Concrete Repositories (Optional)

```csharp
public interface IOutboxReadRepository : IReadRepository<OutboxMessage>
{
    Task<IEnumerable<OutboxMessage>> GetPendingMessagesAsync(int batchSize);
}

public class OutboxReadRepository : ReadRepository<OutboxMessage>, IOutboxReadRepository
{
    public OutboxReadRepository(IDbConnection connection) : base(connection) { }

    public async Task<IEnumerable<OutboxMessage>> GetPendingMessagesAsync(int batchSize)
    {
        var sql = @"SELECT TOP (@BatchSize) * FROM OutboxMessage 
                   WHERE IsPublished = 0 ORDER BY CreatedAt";
        return await QueryAsync(sql, new { BatchSize = batchSize });
    }
}
```

### Step 3: Register in DI Container

In `Program.cs`:

```csharp
services.AddDataAccess(configuration.GetConnectionString("DefaultConnection"));
```

### Step 4: Use in Services

```csharp
public class OutboxProcessingService
{
    private readonly IOutboxReadRepository _readRepo;
    private readonly IOutboxWriteRepository _writeRepo;
    private readonly IUnitOfWork _unitOfWork;

    public OutboxProcessingService(
        IOutboxReadRepository readRepo,
        IOutboxWriteRepository writeRepo,
        IUnitOfWork unitOfWork)
    {
        _readRepo = readRepo;
        _writeRepo = writeRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task ProcessMessagesAsync()
    {
        // Begin transaction for write operations
        _unitOfWork.BeginTransaction();
        try
        {
            // Read (no transaction needed, reads don't participate in transaction)
            var messages = await _readRepo.GetPendingMessagesAsync(100);

            foreach (var msg in messages)
            {
                // Do something...
                await _writeRepo.MarkAsPublishedAsync(msg.MessageId);
            }

            // Commit all writes atomically
            _unitOfWork.Commit();
        }
        catch
        {
            _unitOfWork.Rollback();
            throw;
        }
    }
}
```

## Usage Examples

### Single Key (Auto-increment)

```csharp
[Table("Users")]
public class User
{
    [Key]
    public int UserId { get; set; }
    public string Name { get; set; }
}

// Usage
var user = await readRepo.GetByKeyAsync(123);
await writeRepo.DeleteAsync(123);
```

### Single Key (GUID)

```csharp
[Table("Orders")]
public class Order
{
    [Key]
    public Guid OrderNumber { get; set; }
    public string CustomerName { get; set; }
}

// Usage
var order = await readRepo.GetByKeyAsync(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"));
```

### Composite Key

```csharp
[Table("OrderLines")]
public class OrderLine
{
    [Key]
    public int OrderId { get; set; }
    
    [Key]
    public int LineNumber { get; set; }
    
    public string ProductName { get; set; }
}

// Usage - pass keys in order of [Key] attributes
var orderLine = await readRepo.GetByKeyAsync(456, 2);
await writeRepo.DeleteAsync(456, 2);
```

### Insert

```csharp
var message = new OutboxMessage
{
    Topic = "orders",
    Payload = "{\"orderId\": 123}",
    IsPublished = false
};

// Returns generated ID (for auto-increment) or key value
var id = await writeRepo.InsertAsync(message);
```

### Update

```csharp
var message = new OutboxMessage
{
    MessageId = 123,
    Topic = "orders",
    Payload = "{\"orderId\": 456}",
    IsPublished = false
};

await writeRepo.UpdateAsync(message);
```

### Delete

```csharp
// Single key
await writeRepo.DeleteAsync(123);

// Composite key
await writeRepo.DeleteAsync(456, 2);
```

### Custom Queries

```csharp
// Query multiple
var messages = await readRepo.QueryAsync(
    "SELECT * FROM OutboxMessage WHERE Topic = @Topic",
    new { Topic = "orders" }
);

// Query single
var message = await readRepo.QueryFirstOrDefaultAsync(
    "SELECT TOP 1 * FROM OutboxMessage WHERE IsPublished = 0"
);
```

### Transaction Management

```csharp
_unitOfWork.BeginTransaction();
try
{
    // Multiple write operations - all atomic
    await writeRepo.InsertAsync(entity1);
    await writeRepo.UpdateAsync(entity2);
    await writeRepo.DeleteAsync(id);
    
    _unitOfWork.Commit(); // All succeed or all fail
}
catch
{
    _unitOfWork.Rollback(); // Rollback all changes
    throw;
}
```

## Key Benefits

1. **Separation of Concerns**
   - Read repos don't require transactions
   - Write repos always use transactions
   - Clear responsibility boundaries

2. **Flexible Key Support**
   - Single keys (any type)
   - Composite keys
   - No need to guess - you explicitly define with [Key]

3. **CQRS Ready**
   - Natural split between reads and writes
   - Can route reads to read replicas

4. **Performance**
   - Read connection strings can have different settings
   - No unnecessary transaction overhead on reads
   - Dapper is extremely fast

5. **Testability**
   - Simple interfaces to mock
   - No complex inheritance
   - KeyHelper is static and testable

6. **Flexibility**
   - Use generic base repos or create specific ones
   - Mix and match custom queries
   - Easy to extend with business logic

## File Structure

```
Data/
├── Attributes/
│   ├── KeyAttribute.cs
│   └── TableAttribute.cs
├── Helpers/
│   └── KeyHelper.cs
├── Repositories/
│   ├── IReadRepository.cs
│   ├── ReadRepository.cs
│   ├── IWriteRepository.cs
│   └── WriteRepository.cs
├── UnitOfWork/
│   ├── IUnitOfWork.cs
│   └── UnitOfWork.cs
├── Concrete/
│   └── OutboxRepositories.cs
└── DataExtensions.cs

Models/
└── OutboxMessage.cs

Services/
└── OutboxProcessingService.cs
```

## Next Steps

1. Add more concrete repositories following the OutboxRepositories pattern
2. Create repository factory for dynamic registration
3. Add specification pattern for complex queries
4. Implement async enumeration for large datasets
5. Add caching layer on read repository
6. Implement repository logging/diagnostics
