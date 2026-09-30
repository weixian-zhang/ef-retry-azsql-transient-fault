using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RetryTests;

/// <summary>
/// Test DbContext with EF's SQL Server retry turned on. The interceptor passed in decides what
/// happens when EF opens the connection, so no real database or network is needed.
/// </summary>
public sealed class OrderDbContext : DbContext
{
    public const int MaxRetryCount = 3; // retries, not counting the first attempt

    // Never reached: the interceptor fails or suppresses every open.
    private const string connectionString = "Server=tcp:unreachable.test,1433;Database=Orders;Encrypt=True;";

    private readonly IInterceptor _connectionInterceptor;

    public OrderDbContext(IInterceptor connectionInterceptor) => _connectionInterceptor = connectionInterceptor;

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options
            .UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure(
                maxRetryCount: MaxRetryCount,
                maxRetryDelay: TimeSpan.FromMilliseconds(1), // keep tests fast
                errorNumbersToAdd: null))
            .AddInterceptors(_connectionInterceptor);
}

public sealed class Order
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
}
