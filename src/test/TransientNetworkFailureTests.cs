using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace RetryTests;

/// <summary>
/// Simulates network failures at the exact point EF opens the SQL connection, using a
/// connection interceptor, and checks how EF's execution strategy reacts.
/// </summary>
public sealed class TransientNetworkFailureTests
{
    [Theory]
    [InlineData(233)]   // connection initialization failed before login
    [InlineData(997)]   // overlapped I/O in progress during login
    [InlineData(10060)] // connection attempt timed out
    [InlineData(64)]    // network name no longer available
    [InlineData(10053)] // connection aborted
    [InlineData(10054)] // connection reset by remote host
    public async Task Transient_network_error_is_retried_until_the_connection_opens(int errorNumber)
    {
        var interceptor = new FailingConnectionInterceptor(errorNumber, failuresBeforeSuccess: 2);
        await using var db = new OrderDbContext(interceptor);

        await OpenConnectionWithRetryAsync(db);

        Assert.Equal(3, interceptor.OpenAttempts); // 2 failures + 1 success
    }

    [Fact]
    public async Task Network_that_stays_down_gives_up_after_max_retries()
    {
        var interceptor = new FailingConnectionInterceptor(errorNumber: 10060, failuresBeforeSuccess: int.MaxValue);
        await using var db = new OrderDbContext(interceptor);

        var exception = await Assert.ThrowsAsync<RetryLimitExceededException>(() => OpenConnectionWithRetryAsync(db));

        Assert.Equal(OrderDbContext.MaxRetryCount + 1, interceptor.OpenAttempts);
        Assert.Equal(10060, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [Fact]
    public async Task Non_transient_error_is_not_retried()
    {
        // 18456 "Login failed" (e.g. wrong identity) will fail the same way every time.
        var interceptor = new FailingConnectionInterceptor(errorNumber: 18456, failuresBeforeSuccess: int.MaxValue);
        await using var db = new OrderDbContext(interceptor);

        var exception = await Assert.ThrowsAsync<SqlException>(() => OpenConnectionWithRetryAsync(db));

        Assert.Equal(1, interceptor.OpenAttempts);
        Assert.Equal(18456, exception.Number);
    }

    // Opens the connection inside EF's execution strategy, the same way a transaction or query would.
    private static Task OpenConnectionWithRetryAsync(OrderDbContext db) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.CloseConnectionAsync();
        });
}

/// <summary>
/// Runs immediately before EF opens the connection. Throws a SqlException with the given number
/// for the first attempts, then pretends the open succeeded without touching the network.
/// </summary>
internal sealed class FailingConnectionInterceptor : DbConnectionInterceptor
{
    private readonly int _errorNumber;
    private readonly int _failuresBeforeSuccess;

    public FailingConnectionInterceptor(int errorNumber, int failuresBeforeSuccess)
    {
        _errorNumber = errorNumber;
        _failuresBeforeSuccess = failuresBeforeSuccess;
    }

    public int OpenAttempts { get; private set; }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        OpenAttempts++;

        if (OpenAttempts <= _failuresBeforeSuccess)
        {
            throw SqlExceptionFactory.Create(_errorNumber);
        }

        return ValueTask.FromResult(InterceptionResult.Suppress());
    }
}

/// <summary>
/// SqlException has no public constructor, so tests build one through SqlClient's internal
/// members (the same approach EF Core's own tests use). Written against Microsoft.Data.SqlClient 5.2.
/// </summary>
internal static class SqlExceptionFactory
{
    public static SqlException Create(int errorNumber)
    {
        var errorCollection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var addError = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SqlErrorCollection.Add not found; SqlClient internals changed.");
        addError.Invoke(errorCollection, new object[] { CreateSqlError(errorNumber) });

        var createException = typeof(SqlException).GetMethod(
                "CreateException",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(SqlErrorCollection), typeof(string) },
                modifiers: null)
            ?? throw new InvalidOperationException("SqlException.CreateException not found; SqlClient internals changed.");
        return (SqlException)createException.Invoke(null, new object[] { errorCollection, "16.0" })!;
    }

    private static SqlError CreateSqlError(int errorNumber)
    {
        var constructor = typeof(SqlError)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .First();

        var arguments = constructor.GetParameters()
            .Select(parameter => ArgumentFor(parameter, errorNumber))
            .ToArray();

        return (SqlError)constructor.Invoke(arguments);
    }

    private static object? ArgumentFor(ParameterInfo parameter, int errorNumber) => parameter.Name switch
    {
        "infoNumber" => errorNumber,
        "errorMessage" => $"Simulated SQL error {errorNumber}",
        _ when parameter.ParameterType == typeof(string) => "test",
        _ when parameter.ParameterType == typeof(byte) => (byte)0,
        _ when parameter.ParameterType == typeof(int) => 0,
        _ when parameter.ParameterType == typeof(uint) => 0u,
        _ when !parameter.ParameterType.IsValueType => null,
        _ => throw new InvalidOperationException($"Unexpected SqlError constructor parameter '{parameter.Name}' of type {parameter.ParameterType}."),
    };
}
