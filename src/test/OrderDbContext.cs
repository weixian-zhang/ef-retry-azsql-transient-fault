using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RetryTests;

/// <summary>
/// Test DbContext with retry turned on through DefaultAndCustomTransientErrorRetryExecutionStrategy.
/// The interceptor passed in decides what happens when EF opens the connection, so no real
/// database or network is needed.
/// </summary>
public sealed class OrderDbContext : DbContext
{
    public const int MaxRetryCount = 3; // retries, not counting the first attempt

    // Never reached: the interceptor fails or suppresses every open.
    private const string connectionString = "Server=tcp:unreachable.test,1433;Database=Orders;Encrypt=True;";

    private readonly IInterceptor _connectionInterceptor;

    public OrderDbContext(IInterceptor connectionInterceptor) => _connectionInterceptor = connectionInterceptor;

    public DbSet<Order> Orders => Set<Order>();

    // Opens the connection inside EF's execution strategy, the same way a transaction or query would.
    public Task OpenConnectionWithRetryAsync() =>
        Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await Database.OpenConnectionAsync();
            await Database.CloseConnectionAsync();
        });

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options
            .UseSqlServer(connectionString, sqlServer => sqlServer.ExecutionStrategy(
                dependencies => new DefaultAndCustomTransientErrorRetryExecutionStrategy(
                    dependencies,
                    MaxRetryCount,
                    maxRetryDelay: TimeSpan.FromMilliseconds(1)))) // keep tests fast
            .AddInterceptors(_connectionInterceptor);
}

public sealed class Order
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
}
