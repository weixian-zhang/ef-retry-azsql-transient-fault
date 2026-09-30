using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace SqlClientRetryReview;

/// <summary>
/// EF Core 6 retry with EnableRetryOnFailure. EF retries queries and SaveChanges by itself;
/// a transaction you begin yourself must run inside the execution strategy so the whole
/// transaction is retried as one unit.
/// Do not also set a SqlClient RetryLogicProvider: stacked retries multiply the attempts.
/// </summary>
public static class EntityFrameworkRetryScenario
{
    public static async Task RunAsync(string connectionString)
    {
        Console.WriteLine();
        Console.WriteLine("6. Entity Framework: transaction retried by the execution strategy");
        Console.WriteLine($"  expected: {SqlRetryScenarios.NumberOfTries - 1} retries, then RetryLimitExceededException");

        await using var db = new OrdersDbContext(connectionString);
        var executionStrategy = db.Database.CreateExecutionStrategy();

        try
        {
            // With retry on, calling BeginTransaction outside ExecuteAsync throws
            // InvalidOperationException: EF cannot retry half a transaction.
            await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.Orders.Add(new Order { Status = "Created" });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            Console.WriteLine("  actual:   committed");
        }
        catch (RetryLimitExceededException ex)
        {
            // Every attempt failed with a transient error. The last error is the inner exception.
            Console.WriteLine($"  actual:   gave up, RetryLimitExceededException: {ex.InnerException?.Message}");
        }
        catch (DbUpdateException ex)
        {
            // Not retried: the error is not transient.
            Console.WriteLine($"  actual:   not retried, DbUpdateException: {ex.InnerException?.Message}");
        }
    }
}

internal sealed class OrdersDbContext : DbContext
{
    private readonly string _connectionString;

    public OrdersDbContext(string connectionString) => _connectionString = connectionString;

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options
            .UseSqlServer(_connectionString, sqlServer => sqlServer.EnableRetryOnFailure(
                maxRetryCount: 5, // retries, not counting the first attempt
                maxRetryDelay: TimeSpan.FromSeconds(30),
                // Unlike SqlClient's TransientErrors, this ADDS to EF's built-in list.
                // 208 "Invalid object name" is demo only, so the missing table triggers retries.
                errorNumbersToAdd: new[] { 208 }))
            
            // EF's only retry signal: this event is logged before each retry.
            .LogTo(Console.WriteLine, new[] { CoreEventId.ExecutionStrategyRetrying });

    // Mapped to a table that does not exist, so the insert fails with 208 and nothing is written.
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Order>().ToTable("TableThatDoesNotExist", "dbo");
}

internal sealed class Order
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
}
