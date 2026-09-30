using System;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RetryTests;

/// <summary>
/// EF Core's SQL Server retry strategy, plus one extra transient error: the transport-level error
/// that Linux and macOS report as "error: 0 - Success". Its error number is not on EF's list, so
/// without this EF would not retry it.
/// </summary>
public sealed class DefaultAndCustomTransientErrorRetryExecutionStrategy : SqlServerRetryingExecutionStrategy
{
    public const string TransportErrorZeroMessage =
        "A transport-level error has occurred when receiving results from the server. (provider: TCP Provider, error: 0 - Success)";

    public DefaultAndCustomTransientErrorRetryExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        int maxRetryCount,
        TimeSpan maxRetryDelay)
        : base(dependencies, maxRetryCount, maxRetryDelay, errorNumbersToAdd: null)
    {
    }

    protected override bool ShouldRetryOn(Exception exception)
    {
        // Keep EF Core's built-in transient error detection.
        if (base.ShouldRetryOn(exception))
        {
            return true;
        }

        return exception is SqlException sqlException
            && sqlException.Errors
                .Cast<SqlError>()
                .Any(error => error.Message.Contains(TransportErrorZeroMessage, StringComparison.Ordinal));
    }
}
