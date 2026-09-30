using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlClientRetryReview;

/// <summary>
/// Shows that SqlClient's built-in command retry does nothing inside a transaction, and how the
/// application retries the whole transaction itself instead.
/// </summary>
public static class TransactionRetryScenario
{
    public static async Task RunAsync(string connectionString)
    {
        Console.WriteLine();
        Console.WriteLine("3. Transaction: built-in retry skipped, whole transaction retried by hand");
        Console.WriteLine($"  expected: 0 built-in retries, {SqlRetryScenarios.NumberOfTries - 1} manual retries, then SqlException");

        // The command gets the built-in retry provider on purpose, to prove SqlClient ignores it
        // for a command that has a transaction: this counter stays at 0.
        var commandProvider = SqlRetryScenarios.CreateCommandRetryProvider();
        int builtInRetryCount = 0;
        commandProvider.Retrying += (_, _) => builtInRetryCount++;

        for (int attempt = 1; attempt <= SqlRetryScenarios.NumberOfTries; attempt++)
        {
            try
            {
                // Every attempt gets a new connection and a new transaction, and runs every command again.
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

                await using var command = new SqlCommand($"SELECT * FROM {SqlRetryScenarios.MissingTable}", connection, transaction)
                {
                    CommandTimeout = SqlRetryScenarios.CommandTimeoutSeconds,
                    RetryLogicProvider = commandProvider, // No retry protection: ignored inside a transaction.
                };
                await command.ExecuteScalarAsync();

                await transaction.CommitAsync();
                Console.WriteLine($"  actual:   committed on attempt {attempt}");
                return;
                // Leaving this block without Commit disposes the transaction, which rolls it back.
            }
            catch (SqlException ex) when (IsTransient(ex) && attempt < SqlRetryScenarios.NumberOfTries)
            {
                // Exponential backoff: 2, 4, 8, 16 seconds.
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                Console.WriteLine($"  attempt {attempt} failed with error {ex.Number}, built-in retries: {builtInRetryCount}, retrying whole transaction in {delay.TotalSeconds}s");
                await Task.Delay(delay);
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"  actual:   gave up on attempt {attempt}, built-in retries: {builtInRetryCount}, SqlException: {ex.Message}");
                return;
            }
        }
    }

    internal static bool IsTransient(SqlException exception) =>
        exception.Errors.Cast<SqlError>().Any(error => SqlRetryScenarios.TransientErrorNumbers.Contains(error.Number));
}
