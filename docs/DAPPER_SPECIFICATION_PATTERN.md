# Specification Pattern with Dapper

Comprehensive guide for implementing the Specification pattern with Dapper for flexible and maintainable query building.

## Overview

The Specification pattern allows you to encapsulate query logic in reusable, composable objects. While commonly used with Entity Framework, it can be effectively implemented with Dapper using different strategies.

---

## Table of Contents

1. [SQL Builder Specification](#1-sql-builder-specification-pattern)
2. [Fluent Specification Builder](#2-fluent-specification-builder)
3. [Expression-Based Specifications](#3-expression-based-specifications-advanced)
4. [Repository Pattern Integration](#4-repository-pattern-with-dapper)
5. [Composite Specifications](#5-composite-specifications)
6. [Complete Working Example](#6-complete-working-example)
7. [Best Practices](#7-best-practices)

---

## 1. SQL Builder Specification Pattern

### Interface Definition

```csharp
public interface ISpecification<T>
{
    string ToSql(out DynamicParameters parameters);
}
```

### Concrete Specification

```csharp
public class CustomerSpecification : ISpecification<Customer>
{
    private readonly string? _name;
    private readonly bool? _isActive;
    private readonly DateTime? _createdAfter;
    private readonly int? _minAge;

    public CustomerSpecification(
        string? name = null, 
        bool? isActive = null, 
        DateTime? createdAfter = null,
        int? minAge = null)
    {
        _name = name;
        _isActive = isActive;
        _createdAfter = createdAfter;
        _minAge = minAge;
    }

    public string ToSql(out DynamicParameters parameters)
    {
        parameters = new DynamicParameters();
        var conditions = new List<string>();

        if (!string.IsNullOrEmpty(_name))
        {
            conditions.Add("Name LIKE @Name");
            parameters.Add("Name", $"%{_name}%");
        }

        if (_isActive.HasValue)
        {
            conditions.Add("IsActive = @IsActive");
            parameters.Add("IsActive", _isActive.Value);
        }

        if (_createdAfter.HasValue)
        {
            conditions.Add("CreatedDate > @CreatedAfter");
            parameters.Add("CreatedAfter", _createdAfter.Value);
        }

        if (_minAge.HasValue)
        {
            conditions.Add("Age >= @MinAge");
            parameters.Add("MinAge", _minAge.Value);
        }

        return conditions.Any() 
            ? "WHERE " + string.Join(" AND ", conditions)
            : string.Empty;
    }
}
```

### Repository Usage

```csharp
public class CustomerRepository
{
    private readonly IDbConnection _connection;

    public CustomerRepository(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<IEnumerable<Customer>> GetBySpecificationAsync(ISpecification<Customer> spec)
    {
        var whereSql = spec.ToSql(out var parameters);
        var sql = $"SELECT * FROM Customers {whereSql}";
        
        return await _connection.QueryAsync<Customer>(sql, parameters);
    }

    public async Task<int> CountBySpecificationAsync(ISpecification<Customer> spec)
    {
        var whereSql = spec.ToSql(out var parameters);
        var sql = $"SELECT COUNT(*) FROM Customers {whereSql}";
        
        return await _connection.ExecuteScalarAsync<int>(sql, parameters);
    }
}
```

### Usage Example

```csharp
// Simple usage
var spec = new CustomerSpecification(name: "John", isActive: true);
var customers = await repository.GetBySpecificationAsync(spec);

// Complex filtering
var spec = new CustomerSpecification(
    name: "Smith",
    isActive: true,
    createdAfter: DateTime.Now.AddMonths(-6),
    minAge: 25
);
var customers = await repository.GetBySpecificationAsync(spec);
```

---

## 2. Fluent Specification Builder

### Builder Implementation

```csharp
public class SqlSpecification<T>
{
    private readonly List<string> _conditions = new();
    private readonly DynamicParameters _parameters = new();
    private string? _orderBy;
    private int? _limit;
    private int? _offset;

    public SqlSpecification<T> Where(string condition, object? value = null, string? paramName = null)
    {
        _conditions.Add(condition);
        if (value != null)
        {
            var name = paramName ?? $"p{_parameters.ParameterNames.Count()}";
            _parameters.Add(name, value);
        }
        return this;
    }

    public SqlSpecification<T> And(string condition, object? value = null, string? paramName = null)
    {
        return Where(condition, value, paramName);
    }

    public SqlSpecification<T> Or(string condition, object? value = null, string? paramName = null)
    {
        if (_conditions.Any())
        {
            var last = _conditions[^1];
            _conditions[^1] = $"({last})";
            _conditions.Add($"OR ({condition})");
        }
        else
        {
            _conditions.Add(condition);
        }

        if (value != null)
        {
            var name = paramName ?? $"p{_parameters.ParameterNames.Count()}";
            _parameters.Add(name, value);
        }
        return this;
    }

    public SqlSpecification<T> OrderBy(string column, bool descending = false)
    {
        _orderBy = descending ? $"ORDER BY {column} DESC" : $"ORDER BY {column}";
        return this;
    }

    public SqlSpecification<T> Limit(int limit)
    {
        _limit = limit;
        return this;
    }

    public SqlSpecification<T> Offset(int offset)
    {
        _offset = offset;
        return this;
    }

    public string ToSql()
    {
        var sql = _conditions.Any() 
            ? "WHERE " + string.Join(" AND ", _conditions)
            : string.Empty;

        if (!string.IsNullOrEmpty(_orderBy))
            sql += $" {_orderBy}";

        if (_offset.HasValue)
            sql += $" OFFSET {_offset} ROWS";

        if (_limit.HasValue)
        {
            if (!_offset.HasValue)
                sql += " OFFSET 0 ROWS";
            sql += $" FETCH NEXT {_limit} ROWS ONLY";
        }

        return sql;
    }

    public DynamicParameters GetParameters() => _parameters;
}
```

### Extension Methods

```csharp
public static class DapperSpecificationExtensions
{
    public static async Task<IEnumerable<T>> QueryWithSpecAsync<T>(
        this IDbConnection connection,
        string baseQuery,
        SqlSpecification<T> spec)
    {
        var sql = $"{baseQuery} {spec.ToSql()}";
        return await connection.QueryAsync<T>(sql, spec.GetParameters());
    }

    public static async Task<T?> QueryFirstWithSpecAsync<T>(
        this IDbConnection connection,
        string baseQuery,
        SqlSpecification<T> spec)
    {
        var sql = $"{baseQuery} {spec.ToSql()}";
        return await connection.QueryFirstOrDefaultAsync<T>(sql, spec.GetParameters());
    }

    public static async Task<int> CountWithSpecAsync<T>(
        this IDbConnection connection,
        string tableName,
        SqlSpecification<T> spec)
    {
        var whereSql = spec.ToSql().Replace("ORDER BY.*", "").Trim();
        var sql = $"SELECT COUNT(*) FROM {tableName} {whereSql}";
        return await connection.ExecuteScalarAsync<int>(sql, spec.GetParameters());
    }
}
```

### Usage Example

```csharp
// Simple query
var spec = new SqlSpecification<Customer>()
    .Where("Name LIKE @Name", $"%John%", "Name")
    .And("IsActive = @IsActive", true, "IsActive");

var customers = await connection.QueryWithSpecAsync(
    "SELECT * FROM Customers",
    spec
);

// Complex query with OR, ordering, and pagination
var spec = new SqlSpecification<Customer>()
    .Where("IsActive = @IsActive", true, "IsActive")
    .And("(Name LIKE @Name1", $"%John%", "Name1")
    .Or("Email LIKE @Email1)", $"%john%", "Email1")
    .And("CreatedDate > @CreatedAfter", DateTime.Now.AddMonths(-6), "CreatedAfter")
    .OrderBy("Name")
    .Offset(20)
    .Limit(10);

var customers = await connection.QueryWithSpecAsync(
    "SELECT * FROM Customers",
    spec
);
```

---

## 3. Expression-Based Specifications (Advanced)

### Base Specification

```csharp
using System.Linq.Expressions;

public interface ISpecification<T>
{
    Expression<Func<T, bool>>? Criteria { get; }
    List<Expression<Func<T, object>>> Includes { get; }
    Expression<Func<T, object>>? OrderBy { get; }
    Expression<Func<T, object>>? OrderByDescending { get; }
    int? Take { get; }
    int? Skip { get; }
}

public abstract class BaseSpecification<T> : ISpecification<T>
{
    public Expression<Func<T, bool>>? Criteria { get; private set; }
    public List<Expression<Func<T, object>>> Includes { get; } = new();
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }
    public int? Take { get; private set; }
    public int? Skip { get; private set; }

    protected void SetCriteria(Expression<Func<T, bool>> criteria)
    {
        Criteria = criteria;
    }

    protected void AddInclude(Expression<Func<T, object>> includeExpression)
    {
        Includes.Add(includeExpression);
    }

    protected void SetOrderBy(Expression<Func<T, object>> orderByExpression)
    {
        OrderBy = orderByExpression;
    }

    protected void SetOrderByDescending(Expression<Func<T, object>> orderByDescExpression)
    {
        OrderByDescending = orderByDescExpression;
    }

    protected void SetPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
    }
}
```

### SQL Generator from Expression

```csharp
public class SqlGenerator<T>
{
    private readonly string _tableName;

    public SqlGenerator(string? tableName = null)
    {
        _tableName = tableName ?? typeof(T).Name + "s";
    }

    public (string Sql, DynamicParameters Parameters) Generate(ISpecification<T> spec)
    {
        var parameters = new DynamicParameters();
        var sql = $"SELECT * FROM {_tableName}";

        // WHERE clause
        if (spec.Criteria != null)
        {
            var visitor = new SqlExpressionVisitor(parameters);
            visitor.Visit(spec.Criteria);
            
            if (visitor.WhereClauses.Any())
            {
                sql += " WHERE " + string.Join(" AND ", visitor.WhereClauses);
            }
        }

        // ORDER BY clause
        if (spec.OrderBy != null)
        {
            sql += $" ORDER BY {GetPropertyName(spec.OrderBy)}";
        }
        else if (spec.OrderByDescending != null)
        {
            sql += $" ORDER BY {GetPropertyName(spec.OrderByDescending)} DESC";
        }

        // PAGINATION
        if (spec.Skip.HasValue || spec.Take.HasValue)
        {
            if (!sql.Contains("ORDER BY"))
                sql += " ORDER BY (SELECT NULL)"; // Required for OFFSET/FETCH

            sql += $" OFFSET {spec.Skip ?? 0} ROWS";
            
            if (spec.Take.HasValue)
                sql += $" FETCH NEXT {spec.Take.Value} ROWS ONLY";
        }

        return (sql, parameters);
    }

    private string GetPropertyName(Expression<Func<T, object>> expression)
    {
        if (expression.Body is MemberExpression memberExp)
            return memberExp.Member.Name;
        
        if (expression.Body is UnaryExpression unaryExp && 
            unaryExp.Operand is MemberExpression memberExp2)
            return memberExp2.Member.Name;

        throw new ArgumentException("Invalid expression");
    }
}
```

### SQL Expression Visitor

```csharp
public class SqlExpressionVisitor : ExpressionVisitor
{
    private readonly DynamicParameters _parameters;
    public List<string> WhereClauses { get; } = new();
    private readonly Stack<string> _expressionStack = new();

    public SqlExpressionVisitor(DynamicParameters parameters)
    {
        _parameters = parameters;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.AndAlso)
        {
            Visit(node.Left);
            Visit(node.Right);
            return node;
        }

        if (node.NodeType == ExpressionType.OrElse)
        {
            Visit(node.Left);
            var leftClause = _expressionStack.Pop();
            
            Visit(node.Right);
            var rightClause = _expressionStack.Pop();
            
            _expressionStack.Push($"({leftClause} OR {rightClause})");
            return node;
        }

        var left = GetMemberName(node.Left);
        var right = GetValue(node.Right);
        
        var paramName = $"p{_parameters.ParameterNames.Count()}";
        
        var op = node.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            _ => throw new NotSupportedException($"Operation {node.NodeType} not supported")
        };

        if (right == null)
        {
            var clause = op == "=" ? $"{left} IS NULL" : $"{left} IS NOT NULL";
            WhereClauses.Add(clause);
            _expressionStack.Push(clause);
        }
        else
        {
            _parameters.Add(paramName, right);
            var clause = $"{left} {op} @{paramName}";
            WhereClauses.Add(clause);
            _expressionStack.Push(clause);
        }
        
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == "Contains" && node.Object != null)
        {
            var memberName = GetMemberName(node.Object);
            var value = GetValue(node.Arguments[0]);
            
            var paramName = $"p{_parameters.ParameterNames.Count()}";
            _parameters.Add(paramName, $"%{value}%");
            
            var clause = $"{memberName} LIKE @{paramName}";
            WhereClauses.Add(clause);
            _expressionStack.Push(clause);
            
            return node;
        }

        if (node.Method.Name == "StartsWith" && node.Object != null)
        {
            var memberName = GetMemberName(node.Object);
            var value = GetValue(node.Arguments[0]);
            
            var paramName = $"p{_parameters.ParameterNames.Count()}";
            _parameters.Add(paramName, $"{value}%");
            
            var clause = $"{memberName} LIKE @{paramName}";
            WhereClauses.Add(clause);
            _expressionStack.Push(clause);
            
            return node;
        }

        if (node.Method.Name == "EndsWith" && node.Object != null)
        {
            var memberName = GetMemberName(node.Object);
            var value = GetValue(node.Arguments[0]);
            
            var paramName = $"p{_parameters.ParameterNames.Count()}";
            _parameters.Add(paramName, $"%{value}");
            
            var clause = $"{memberName} LIKE @{paramName}";
            WhereClauses.Add(clause);
            _expressionStack.Push(clause);
            
            return node;
        }

        return base.VisitMethodCall(node);
    }

    private string GetMemberName(Expression expression)
    {
        if (expression is MemberExpression memberExp)
            return memberExp.Member.Name;
        
        if (expression is UnaryExpression unaryExp && unaryExp.Operand is MemberExpression memberExp2)
            return memberExp2.Member.Name;
            
        throw new ArgumentException("Must be a member expression");
    }

    private object? GetValue(Expression expression)
    {
        if (expression is ConstantExpression constantExp)
            return constantExp.Value;
        
        var lambda = Expression.Lambda(expression);
        return lambda.Compile().DynamicInvoke();
    }
}
```

### Concrete Specification Examples

```csharp
public class ActiveCustomersSpec : BaseSpecification<Customer>
{
    public ActiveCustomersSpec()
    {
        SetCriteria(c => c.IsActive);
        SetOrderBy(c => c.Name);
    }
}

public class CustomersByNameSpec : BaseSpecification<Customer>
{
    public CustomersByNameSpec(string nameFilter)
    {
        SetCriteria(c => c.Name.Contains(nameFilter) && c.IsActive);
        SetOrderBy(c => c.CreatedDate);
    }
}

public class RecentCustomersSpec : BaseSpecification<Customer>
{
    public RecentCustomersSpec(int days = 30, int pageSize = 10, int pageNumber = 1)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-days);
        SetCriteria(c => c.CreatedDate > cutoffDate);
        SetOrderByDescending(c => c.CreatedDate);
        SetPaging((pageNumber - 1) * pageSize, pageSize);
    }
}
```

### Usage

```csharp
// Repository method
public async Task<IEnumerable<T>> FindAsync(ISpecification<T> specification)
{
    var generator = new SqlGenerator<T>();
    var (sql, parameters) = generator.Generate(specification);
    
    return await _connection.QueryAsync<T>(sql, parameters);
}

// Usage
var spec = new CustomersByNameSpec("John");
var customers = await repository.FindAsync(spec);

var recentSpec = new RecentCustomersSpec(days: 7, pageSize: 20, pageNumber: 2);
var recentCustomers = await repository.FindAsync(recentSpec);
```

---

## 4. Repository Pattern with Dapper

### Generic Repository Interface

```csharp
public interface IRepository<T> where T : class
{
    Task<IEnumerable<T>> FindAsync(ISpecification<T> specification);
    Task<T?> FindOneAsync(ISpecification<T> specification);
    Task<int> CountAsync(ISpecification<T> specification);
    Task<bool> ExistsAsync(ISpecification<T> specification);
}
```

### Generic Repository Implementation

```csharp
public class DapperRepository<T> : IRepository<T> where T : class
{
    private readonly IDbConnection _connection;
    private readonly string _tableName;

    public DapperRepository(IDbConnection connection, string? tableName = null)
    {
        _connection = connection;
        _tableName = tableName ?? typeof(T).Name + "s";
    }

    public async Task<IEnumerable<T>> FindAsync(ISpecification<T> specification)
    {
        var whereSql = specification.ToSql(out var parameters);
        var sql = $"SELECT * FROM {_tableName} {whereSql}";
        
        return await _connection.QueryAsync<T>(sql, parameters);
    }

    public async Task<T?> FindOneAsync(ISpecification<T> specification)
    {
        var whereSql = specification.ToSql(out var parameters);
        var sql = $"SELECT TOP 1 * FROM {_tableName} {whereSql}";
        
        return await _connection.QueryFirstOrDefaultAsync<T>(sql, parameters);
    }

    public async Task<int> CountAsync(ISpecification<T> specification)
    {
        var whereSql = specification.ToSql(out var parameters);
        var sql = $"SELECT COUNT(*) FROM {_tableName} {whereSql}";
        
        return await _connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    public async Task<bool> ExistsAsync(ISpecification<T> specification)
    {
        var count = await CountAsync(specification);
        return count > 0;
    }
}
```

### Specific Repository

```csharp
public interface ICustomerRepository : IRepository<Customer>
{
    Task<IEnumerable<Customer>> GetActiveCustomersAsync();
    Task<Customer?> GetByEmailAsync(string email);
}

public class CustomerRepository : DapperRepository<Customer>, ICustomerRepository
{
    private readonly IDbConnection _connection;

    public CustomerRepository(IDbConnection connection) 
        : base(connection, "Customers")
    {
        _connection = connection;
    }

    public async Task<IEnumerable<Customer>> GetActiveCustomersAsync()
    {
        var spec = new CustomerSpecification(isActive: true);
        return await FindAsync(spec);
    }

    public async Task<Customer?> GetByEmailAsync(string email)
    {
        var sql = "SELECT * FROM Customers WHERE Email = @Email";
        return await _connection.QueryFirstOrDefaultAsync<Customer>(sql, new { Email = email });
    }
}
```

---

## 5. Composite Specifications

### AND Specification

```csharp
public class AndSpecification<T> : ISpecification<T>
{
    private readonly ISpecification<T> _left;
    private readonly ISpecification<T> _right;

    public AndSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        _left = left;
        _right = right;
    }

    public string ToSql(out DynamicParameters parameters)
    {
        var leftSql = _left.ToSql(out var leftParams);
        var rightSql = _right.ToSql(out var rightParams);

        parameters = new DynamicParameters(leftParams);
        
        // Rename parameters to avoid conflicts
        foreach (var paramName in rightParams.ParameterNames)
        {
            parameters.Add($"right_{paramName}", rightParams.Get<object>(paramName));
        }

        var leftCondition = leftSql.Replace("WHERE ", "").Trim();
        var rightCondition = rightSql.Replace("WHERE ", "").Replace("@", "@right_").Trim();

        if (string.IsNullOrEmpty(leftCondition) && string.IsNullOrEmpty(rightCondition))
            return string.Empty;
        
        if (string.IsNullOrEmpty(leftCondition))
            return $"WHERE {rightCondition}";
        
        if (string.IsNullOrEmpty(rightCondition))
            return $"WHERE {leftCondition}";

        return $"WHERE ({leftCondition}) AND ({rightCondition})";
    }
}
```

### OR Specification

```csharp
public class OrSpecification<T> : ISpecification<T>
{
    private readonly ISpecification<T> _left;
    private readonly ISpecification<T> _right;

    public OrSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        _left = left;
        _right = right;
    }

    public string ToSql(out DynamicParameters parameters)
    {
        var leftSql = _left.ToSql(out var leftParams);
        var rightSql = _right.ToSql(out var rightParams);

        parameters = new DynamicParameters(leftParams);
        
        foreach (var paramName in rightParams.ParameterNames)
        {
            parameters.Add($"right_{paramName}", rightParams.Get<object>(paramName));
        }

        var leftCondition = leftSql.Replace("WHERE ", "").Trim();
        var rightCondition = rightSql.Replace("WHERE ", "").Replace("@", "@right_").Trim();

        if (string.IsNullOrEmpty(leftCondition) && string.IsNullOrEmpty(rightCondition))
            return string.Empty;
        
        if (string.IsNullOrEmpty(leftCondition))
            return $"WHERE {rightCondition}";
        
        if (string.IsNullOrEmpty(rightCondition))
            return $"WHERE {leftCondition}";

        return $"WHERE ({leftCondition}) OR ({rightCondition})";
    }
}
```

### NOT Specification

```csharp
public class NotSpecification<T> : ISpecification<T>
{
    private readonly ISpecification<T> _specification;

    public NotSpecification(ISpecification<T> specification)
    {
        _specification = specification;
    }

    public string ToSql(out DynamicParameters parameters)
    {
        var sql = _specification.ToSql(out parameters);
        var condition = sql.Replace("WHERE ", "").Trim();

        return string.IsNullOrEmpty(condition) 
            ? string.Empty 
            : $"WHERE NOT ({condition})";
    }
}
```

### Extension Methods

```csharp
public static class SpecificationExtensions
{
    public static ISpecification<T> And<T>(this ISpecification<T> left, ISpecification<T> right)
    {
        return new AndSpecification<T>(left, right);
    }

    public static ISpecification<T> Or<T>(this ISpecification<T> left, ISpecification<T> right)
    {
        return new OrSpecification<T>(left, right);
    }

    public static ISpecification<T> Not<T>(this ISpecification<T> specification)
    {
        return new NotSpecification<T>(specification);
    }
}
```

### Usage

```csharp
var nameSpec = new CustomerNameSpecification("John");
var activeSpec = new ActiveCustomerSpecification();
var recentSpec = new RecentCustomerSpecification(DateTime.Now.AddMonths(-1));

// Combine specifications
var combinedSpec = nameSpec.And(activeSpec).And(recentSpec);
var customers = await repository.FindAsync(combinedSpec);

// OR combination
var johnOrActive = nameSpec.Or(activeSpec);
var customers = await repository.FindAsync(johnOrActive);

// NOT
var notActive = activeSpec.Not();
var inactiveCustomers = await repository.FindAsync(notActive);
```

---

## 6. Complete Working Example

### Domain Model

```csharp
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public int Age { get; set; }
    public decimal Balance { get; set; }
}
```

### Specifications

```csharp
public class ActiveCustomerSpec : ISpecification<Customer>
{
    public string ToSql(out DynamicParameters parameters)
    {
        parameters = new DynamicParameters();
        parameters.Add("IsActive", true);
        return "WHERE IsActive = @IsActive";
    }
}

public class CustomerNameSpec : ISpecification<Customer>
{
    private readonly string _name;

    public CustomerNameSpec(string name)
    {
        _name = name;
    }

    public string ToSql(out DynamicParameters parameters)
    {
        parameters = new DynamicParameters();
        parameters.Add("Name", $"%{_name}%");
        return "WHERE Name LIKE @Name";
    }
}

public class CustomerAgeRangeSpec : ISpecification<Customer>
{
    private readonly int _minAge;
    private readonly int _maxAge;

    public CustomerAgeRangeSpec(int minAge, int maxAge)
    {
        _minAge = minAge;
        _maxAge = maxAge;
    }

    public string ToSql(out DynamicParameters parameters)
    {
        parameters = new DynamicParameters();
        parameters.Add("MinAge", _minAge);
        parameters.Add("MaxAge", _maxAge);
        return "WHERE Age BETWEEN @MinAge AND @MaxAge";
    }
}

public class HighValueCustomerSpec : ISpecification<Customer>
{
    private readonly decimal _minBalance;

    public HighValueCustomerSpec(decimal minBalance = 10000)
    {
        _minBalance = minBalance;
    }

    public string ToSql(out DynamicParameters parameters)
    {
        parameters = new DynamicParameters();
        parameters.Add("MinBalance", _minBalance);
        return "WHERE Balance >= @MinBalance";
    }
}
```

### Service Layer

```csharp
public class CustomerService
{
    private readonly ICustomerRepository _repository;

    public CustomerService(ICustomerRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Customer>> GetActiveCustomersByNameAsync(string name)
    {
        var spec = new CustomerNameSpec(name).And(new ActiveCustomerSpec());
        return await _repository.FindAsync(spec);
    }

    public async Task<IEnumerable<Customer>> GetHighValueActiveCustomersAsync()
    {
        var spec = new HighValueCustomerSpec()
            .And(new ActiveCustomerSpec());
        
        return await _repository.FindAsync(spec);
    }

    public async Task<IEnumerable<Customer>> GetYoungHighValueCustomersAsync()
    {
        var spec = new CustomerAgeRangeSpec(18, 35)
            .And(new HighValueCustomerSpec(50000))
            .And(new ActiveCustomerSpec());
        
        return await _repository.FindAsync(spec);
    }

    public async Task<int> CountActiveCustomersAsync()
    {
        var spec = new ActiveCustomerSpec();
        return await _repository.CountAsync(spec);
    }
}
```

### Dependency Injection Setup

```csharp
// Program.cs
builder.Services.AddScoped<IDbConnection>(sp => 
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<CustomerService>();
```

### Controller Usage

```csharp
[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly CustomerService _customerService;

    public CustomersController(CustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchByName([FromQuery] string name)
    {
        var customers = await _customerService.GetActiveCustomersByNameAsync(name);
        return Ok(customers);
    }

    [HttpGet("high-value")]
    public async Task<IActionResult> GetHighValueCustomers()
    {
        var customers = await _customerService.GetHighValueActiveCustomersAsync();
        return Ok(customers);
    }

    [HttpGet("young-wealthy")]
    public async Task<IActionResult> GetYoungWealthyCustomers()
    {
        var customers = await _customerService.GetYoungHighValueCustomersAsync();
        return Ok(customers);
    }

    [HttpGet("count")]
    public async Task<IActionResult> GetActiveCount()
    {
        var count = await _customerService.CountActiveCustomersAsync();
        return Ok(new { count });
    }
}
```

---

## 7. Best Practices

### 1. Keep Specifications Focused

```csharp
// ✅ Good - Single responsibility
public class ActiveCustomerSpec : ISpecification<Customer>
{
    public string ToSql(out DynamicParameters parameters)
    {
        parameters = new DynamicParameters();
        parameters.Add("IsActive", true);
        return "WHERE IsActive = @IsActive";
    }
}

// ❌ Bad - Too many responsibilities
public class ComplexCustomerSpec : ISpecification<Customer>
{
    private readonly bool _isActive;
    private readonly string? _name;
    private readonly int? _minAge;
    private readonly decimal? _minBalance;
    // ... 10 more fields
}
```

### 2. Use Composition Over Complex Specifications

```csharp
// ✅ Good - Compose small specifications
var spec = new ActiveCustomerSpec()
    .And(new CustomerNameSpec("John"))
    .And(new HighValueCustomerSpec());

// ❌ Bad - One giant specification
var spec = new ActiveCustomerByNameAndHighValueSpec("John", 10000);
```

### 3. SQL Injection Prevention

```csharp
// ✅ Good - Always use parameters
public string ToSql(out DynamicParameters parameters)
{
    parameters = new DynamicParameters();
    parameters.Add("Name", $"%{_name}%");
    return "WHERE Name LIKE @Name";
}

// ❌ Bad - String concatenation
public string ToSql(out DynamicParameters parameters)
{
    parameters = new DynamicParameters();
    return $"WHERE Name LIKE '%{_name}%'"; // SQL Injection risk!
}
```

### 4. Handle Empty Specifications

```csharp
public string ToSql(out DynamicParameters parameters)
{
    parameters = new DynamicParameters();
    var conditions = new List<string>();

    if (!string.IsNullOrEmpty(_name))
    {
        conditions.Add("Name LIKE @Name");
        parameters.Add("Name", $"%{_name}%");
    }

    // Return empty string if no conditions, not "WHERE "
    return conditions.Any() 
        ? "WHERE " + string.Join(" AND ", conditions)
        : string.Empty;
}
```

### 5. Naming Conventions

```csharp
// ✅ Good - Clear, descriptive names
ActiveCustomerSpec
CustomersByNameSpec
RecentOrdersSpec
HighValueCustomerSpec

// ❌ Bad - Vague names
CustomerSpec1
MySpec
DataFilter
```

### 6. Unit Testing

```csharp
[Fact]
public void ActiveCustomerSpec_GeneratesCorrectSql()
{
    // Arrange
    var spec = new ActiveCustomerSpec();

    // Act
    var sql = spec.ToSql(out var parameters);

    // Assert
    Assert.Equal("WHERE IsActive = @IsActive", sql);
    Assert.True(parameters.Get<bool>("IsActive"));
}

[Fact]
public void CompositeSpec_CombinesCorrectly()
{
    // Arrange
    var nameSpec = new CustomerNameSpec("John");
    var activeSpec = new ActiveCustomerSpec();
    var compositeSpec = nameSpec.And(activeSpec);

    // Act
    var sql = compositeSpec.ToSql(out var parameters);

    // Assert
    Assert.Contains("Name LIKE @Name", sql);
    Assert.Contains("IsActive = @IsActive", sql);
    Assert.Contains("AND", sql);
}
```

### 7. Performance Considerations

```csharp
// ✅ Good - Add indexes for common specifications
CREATE INDEX IX_Customers_IsActive ON Customers(IsActive);
CREATE INDEX IX_Customers_Name ON Customers(Name);

// ✅ Good - Use covering indexes when possible
CREATE INDEX IX_Customers_Active_Name_Covering 
ON Customers(IsActive, Name) INCLUDE (Email, CreatedDate);
```

### 8. Repository Method Naming

```csharp
public interface IRepository<T>
{
    // ✅ Good - Clear intent
    Task<IEnumerable<T>> FindAsync(ISpecification<T> specification);
    Task<T?> FindOneAsync(ISpecification<T> specification);
    Task<int> CountAsync(ISpecification<T> specification);
    Task<bool> ExistsAsync(ISpecification<T> specification);
    
    // ❌ Bad - Unclear
    Task<IEnumerable<T>> GetAsync(ISpecification<T> specification);
    Task<T?> SingleAsync(ISpecification<T> specification);
}
```

---

## 8. Advanced Patterns

### Cached Specifications

```csharp
public class CachedSpecification<T> : ISpecification<T>
{
    private readonly ISpecification<T> _innerSpec;
    private string? _cachedSql;
    private DynamicParameters? _cachedParameters;

    public CachedSpecification(ISpecification<T> innerSpec)
    {
        _innerSpec = innerSpec;
    }

    public string ToSql(out DynamicParameters parameters)
    {
        if (_cachedSql == null)
        {
            _cachedSql = _innerSpec.ToSql(out _cachedParameters!);
        }

        parameters = _cachedParameters!;
        return _cachedSql;
    }
}
```

### Specification with Ordering

```csharp
public interface IOrderedSpecification<T> : ISpecification<T>
{
    string OrderByClause { get; }
}

public class OrderedCustomerSpec : IOrderedSpecification<Customer>
{
    private readonly string _orderBy;

    public OrderedCustomerSpec(string orderBy = "Name")
    {
        _orderBy = orderBy;
    }

    public string OrderByClause => $"ORDER BY {_orderBy}";

    public string ToSql(out DynamicParameters parameters)
    {
        parameters = new DynamicParameters();
        parameters.Add("IsActive", true);
        return $"WHERE IsActive = @IsActive {OrderByClause}";
    }
}
```

### Paginated Specification

```csharp
public class PagedSpecification<T> : ISpecification<T>
{
    private readonly ISpecification<T> _innerSpec;
    private readonly int _pageNumber;
    private readonly int _pageSize;

    public PagedSpecification(ISpecification<T> innerSpec, int pageNumber, int pageSize)
    {
        _innerSpec = innerSpec;
        _pageNumber = pageNumber;
        _pageSize = pageSize;
    }

    public string ToSql(out DynamicParameters parameters)
    {
        var sql = _innerSpec.ToSql(out parameters);
        
        var offset = (_pageNumber - 1) * _pageSize;
        sql += $" OFFSET {offset} ROWS FETCH NEXT {_pageSize} ROWS ONLY";
        
        return sql;
    }
}

// Usage
var baseSpec = new ActiveCustomerSpec();
var pagedSpec = new PagedSpecification<Customer>(baseSpec, pageNumber: 2, pageSize: 20);
```

---

## 9. Comparison of Approaches

| Approach | Pros | Cons | Best For |
|----------|------|------|----------|
| **SQL Builder** | Simple, direct SQL, easy to debug | Manual SQL writing | Most projects |
| **Fluent Builder** | Flexible, chainable, readable | Less type-safe | Dynamic queries |
| **Expression-Based** | Type-safe, refactorable | Complex, harder to debug | Large enterprise apps |
| **Composite** | Reusable, composable | Can get complex | Complex business logic |

---

## 10. Recommended Approach

For most projects, use **SQL Builder Specification** (Option 1) because:

✅ Simple and maintainable  
✅ Performance optimal (direct SQL)  
✅ Easy to debug  
✅ Dapper-friendly  
✅ No complex expression parsing  
✅ Clear SQL visibility  

Use **Expression-Based** only if:
- You need database abstraction
- You're migrating from EF Core
- You have very complex domain logic

---

## Resources

- [Dapper Documentation](https://github.com/DapperLib/Dapper)
- [Specification Pattern (Martin Fowler)](https://www.martinfowler.com/apsupp/spec.pdf)
- [Repository Pattern](https://docs.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-design)

---

**Last Updated**: January 25, 2026
