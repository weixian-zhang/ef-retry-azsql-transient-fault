using System;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RetryTests;

/// <summary>
/// EF Core's SQL Server retry strategy, plus extra transient errors matched by message, such as the
/// transport-level error that Linux and macOS report as "error: 0 - Success". Their error numbers are
/// not on EF's list, so without this EF would not retry them.
/// </summary>
public sealed class DefaultAndCustomTransientErrorRetryExecutionStrategy : SqlServerRetryingExecutionStrategy
{
    // A SqlError whose message contains any of these is retried. Add more messages here.
    public static readonly string[] TransientErrorMessages =
    {
        "A transport-level error has occurred when receiving results from the server. (provider: TCP Provider, error: 0 - Success)",
    };

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

        // Add custom SQL client errors.
        if (exception is SqlException sqlException)
        {
            foreach (SqlError error in sqlException.Errors)
            {
                foreach (var transientMessage in TransientErrorMessages)
                {
                    if (error.Message.Contains(transientMessage, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // EF calls this just before each retry, never on the first attempt or after the last failure.
    protected override void OnRetry()
    {
        var retryNumber = ExceptionsEncountered.Count;
        var lastError = ExceptionsEncountered[^1];
        // A SaveChanges failure arrives as DbUpdateException with the SqlException inside it.
        var sqlException = lastError as SqlException ?? lastError.InnerException as SqlException;
        var error = sqlException != null
            ? $"SqlException {sqlException.Number}: {sqlException.Message}"
            : $"{lastError.GetType().Name}: {lastError.Message}";

        Console.WriteLine(
            $"[{DateTime.Now:HH:mm:ss.fff}] EF retry {retryNumber} of {MaxRetryCount} " +
            $"in {_nextDelay?.TotalMilliseconds:F0} ms after {error}");
    }

    // EF calls this before OnRetry; kept only so OnRetry can log the delay.
    protected override TimeSpan? GetNextDelay(Exception lastException)
    {
        _nextDelay = base.GetNextDelay(lastException);
        return _nextDelay;
    }

    private TimeSpan? _nextDelay;
}
