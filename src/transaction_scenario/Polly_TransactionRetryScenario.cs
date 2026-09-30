using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;

namespace SqlClientRetryReview;

/// <summary>
/// The same whole-transaction retry as TransactionRetryScenario, with Polly doing the retry loop,
/// backoff and attempt counting instead of a hand-written for loop.
/// Do not also put a SqlClient RetryLogicProvider on this connection: stacked retries multiply
/// the attempts (5 x 5).
/// </summary>
public static class PollyTransactionRetryScenario
{
    public static async Task RunAsync(string connectionString)
    {
        Console.WriteLine();
        Console.WriteLine("4. Transaction retried with Polly instead of a for loop");
        Console.WriteLine($"  expected: {SqlRetryScenarios.NumberOfTries - 1} Polly retries, then SqlException");

        var transactionRetryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<SqlException>(TransactionRetryScenario.IsTransient),
                MaxRetryAttempts = 5, // retries, not counting the first attempt
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2), // 2, 4, 8, 16 seconds
                UseJitter = true, // true in production, so many clients don't retry in lockstep
                OnRetry = retryArguments =>
                {
                    Console.WriteLine($"  attempt {retryArguments.AttemptNumber + 1} failed: {retryArguments.Outcome.Exception?.Message} Retrying whole transaction in {retryArguments.RetryDelay.TotalSeconds}s");
                    return default;
                },
            })
            .Build();

        try
        {
            await transactionRetryPipeline.ExecuteAsync(async cancellationToken =>
            {
                // Every attempt gets a new connection and a new transaction, and runs every command again.
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

                await using var command = new SqlCommand($"SELECT * FROM {SqlRetryScenarios.MissingTable}", connection, transaction)
                {
                    CommandTimeout = SqlRetryScenarios.CommandTimeoutSeconds,
                };
                await command.ExecuteScalarAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                // Leaving this block without Commit disposes the transaction, which rolls it back.
            });

            Console.WriteLine("  actual:   committed");
        }
        catch (SqlException ex)
        {
            // After the last retry Polly rethrows the original SqlException, not an AggregateException.
            Console.WriteLine($"  actual:   gave up, SqlException: {ex.Message}");
        }
    }
}
