using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RetryTests;

/// <summary>
/// Test DbContext with retry turned on through DefaultAndCustomTransientErrorRetryExecutionStrategy.
/// Built with an interceptor, it needs no real database: the interceptor decides what happens when
/// EF opens the connection. Built with a connection string, it talks to that real database.
/// </summary>
public sealed class OrderDbContext : DbContext
{
    public const int MaxRetryCount = 3; // retries, not counting the first attempt

    // Never reached: the interceptor fails or suppresses every open.
    private const string UnreachableConnectionString = "Server=tcp:unreachable.test,1433;Database=Orders;Encrypt=True;";

    private readonly string _connectionString;
    private readonly IInterceptor? _connectionInterceptor;

    public OrderDbContext(IInterceptor connectionInterceptor)
    {
        _connectionString = UnreachableConnectionString;
        _connectionInterceptor = connectionInterceptor;
    }

    public OrderDbContext(string connectionString) => _connectionString = connectionString;

    public DbSet<Order> Orders => Set<Order>();

    // Opens the connection inside EF's execution strategy, the same way a transaction or query would.
    public Task OpenConnectionWithRetryAsync() =>
        Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await Database.OpenConnectionAsync();
            await Database.CloseConnectionAsync();
        });

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlServer(_connectionString, sqlServer => sqlServer.ExecutionStrategy(
            dependencies => new DefaultAndCustomTransientErrorRetryExecutionStrategy(
                dependencies,
                MaxRetryCount,
                maxRetryDelay: TimeSpan.FromMilliseconds(1)))); // keep tests fast

        if (_connectionInterceptor != null)
        {
            options.AddInterceptors(_connectionInterceptor);
        }
    }
}

public sealed class Order
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
}
