using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RetryTests;

/// <summary>
/// Runs immediately before EF opens the connection. Throws a SqlException with the given number
/// and message for the first attempts, then pretends the open succeeded without touching the network.
/// </summary>
internal sealed class FailingConnectionInterceptor : DbConnectionInterceptor
{
    private readonly int _errorNumber;
    private readonly string _errorMessage;
    private readonly int _failuresBeforeSuccess;

    public FailingConnectionInterceptor(int errorNumber, int failuresBeforeSuccess, string? errorMessage = null)
    {
        _errorNumber = errorNumber;
        _errorMessage = errorMessage ?? $"Simulated SQL error {errorNumber}";
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
            throw SqlExceptionFactory.Create(_errorNumber, _errorMessage);
        }

        return ValueTask.FromResult(InterceptionResult.Suppress());
    }
}
