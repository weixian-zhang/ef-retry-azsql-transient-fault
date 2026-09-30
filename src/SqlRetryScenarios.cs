using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlClientRetryReview;

/// <summary>
/// Shows when Microsoft.Data.SqlClient's built-in exponential retry fires and when it does not.
///
/// Package: Microsoft.Data.SqlClient 4.0 or later (5.2.x recommended for .NET 6).
/// On 3.x the retry providers are silently ignored unless the app first calls
/// AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.EnableRetryLogic", true).
///
/// Usage: await SqlRetryScenarios.RunAllAsync(connectionString);
/// The connection string must point at a reachable server and database.
/// </summary>
public static class SqlRetryScenarios
{
    // Production baseline for Azure SQL: 1 initial attempt + 4 retries, 2s base delay,
    // no single delay longer than 30s. Delays grow exponentially with random jitter;
    // the Retrying output prints the real values.
    internal const int NumberOfTries = 5;
    private static readonly TimeSpan DeltaTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxTimeInterval = TimeSpan.FromSeconds(30);

    // Connect Timeout (Open) and CommandTimeout (Execute*) apply to EACH attempt, so the
    // worst case is roughly NumberOfTries x timeout + all delays. CommandTimeout = 0 means
    // "wait forever" and is a review red flag.
    private const int ConnectTimeoutSeconds = 30;
    internal const int CommandTimeoutSeconds = 30;

    // Error 208 "Invalid object name" is NOT transient in real life. It is in the list only so
    // that querying a missing table triggers the retry path on demand.
    private const int InvalidObjectNameError = 208;
    internal const string MissingTable = "dbo.TableThatDoesNotExist";

    // Errors retried by the command provider and by TransactionRetryScenario's retry loop.
    internal static readonly int[] TransientErrorNumbers =
    {
        1205,  // deadlock victim
        4060,  // cannot open database (failover, restore, scaling)
        10928, // resource limit reached
        10929, // server too busy
        40143, // Azure SQL service error
        40197, // Azure SQL failover
        40501, // Azure SQL service busy
        40613, // database not currently available
        49918, // not enough resources
        49919, // too many create/update operations
        49920, // too many operations
        InvalidObjectNameError, // demo only, see above
    };

    public static async Task RunAllAsync(string connectionString)
    {
        connectionString = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = ConnectTimeoutSeconds,
        }.ConnectionString;

        await ConnectionAndCommandRetry(connectionString);
        await CommandRetry_SkipsNonTransientError(connectionString);
        await TransactionRetryScenario.RunAsync(connectionString);
        await PollyTransactionRetryScenario.RunAsync(connectionString);
        await StoredProcedureRetryScenario.RunAsync(connectionString);
        await EntityFrameworkRetryScenario.RunAsync(connectionString);
        await EntityFrameworkCustomRetryStrategyScenario.RunAsync(connectionString);
    }

    /// <summary>
    /// Retries SqlConnection.Open/OpenAsync only. TransientErrors is left null, so SqlClient uses
    /// its built-in transient list (4060, 40197, 40501, 40613, 10928, 10929, 1205, ...).
    /// The backoff shape comes from the factory method, not from SqlRetryLogicOption:
    /// CreateFixedRetryProvider, CreateIncrementalRetryProvider and CreateNoneRetryProvider
    /// read the same options differently.
    /// </summary>
    public static SqlRetryLogicBaseProvider CreateConnectionRetryProvider()
    {
        var connectionRetryOptions = new SqlRetryLogicOption
        {
            NumberOfTries = NumberOfTries,
            DeltaTime = DeltaTime,
            MaxTimeInterval = MaxTimeInterval,
        };

        return SqlConfigurableRetryFactory.CreateExponentialRetryProvider(connectionRetryOptions);
    }

    /// <summary>
    /// Retries SqlCommand.Execute* (NonQuery, Reader, Scalar, XmlReader and their async forms).
    /// Setting TransientErrors REPLACES the built-in list; it does not add to it.
    /// </summary>
    public static SqlRetryLogicBaseProvider CreateCommandRetryProvider()
    {
        var commandRetryOptions = new SqlRetryLogicOption
        {
            NumberOfTries = NumberOfTries,
            DeltaTime = DeltaTime,
            MaxTimeInterval = MaxTimeInterval,
            TransientErrors = TransientErrorNumbers,
            // Only commands this predicate accepts are retried; the rest run once.
            // Retrying writes can duplicate data, so only reads are allowed here.
            AuthorizedSqlCondition = commandText =>
                commandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase),
        };

        return SqlConfigurableRetryFactory.CreateExponentialRetryProvider(commandRetryOptions);
    }

    // Scenario 1 — connection and command retry providers together, with its own try/catch.
    // Open succeeds, so the connection provider does not retry. The SELECT fails with 208, which
    // the command provider retries: 208 is in TransientErrors and AuthorizedSqlCondition allows
    // SELECT. An INSERT failing with the same error would run once.
    private static async Task ConnectionAndCommandRetry(string connectionString)
    {
        Console.WriteLine();
        Console.WriteLine("1. Connection and command retry providers together");
        Console.WriteLine($"  expected: 0 connection retries, {NumberOfTries - 1} command retries, then AggregateException");

        var connectionRetryProvider = CreateConnectionRetryProvider();
        var commandRetryProvider = CreateCommandRetryProvider();

        int connectionRetryCount = 0;
        int commandRetryCount = 0;

        connectionRetryProvider.Retrying += (object? sender, SqlRetryingEventArgs args) =>
        {
            connectionRetryCount++;
            var lastError = args.Exceptions[args.Exceptions.Count - 1];
            Console.WriteLine($"  connection retry #{args.RetryCount} in {args.Delay.TotalSeconds:F1}s after: {lastError.Message}");
        };
        
        commandRetryProvider.Retrying += (object? sender, SqlRetryingEventArgs args) =>
        {
            commandRetryCount++;
            var lastError = args.Exceptions[args.Exceptions.Count - 1];
            Console.WriteLine($"  command retry #{args.RetryCount} in {args.Delay.TotalSeconds:F1}s after: {lastError.Message}");
        };

        try
        {
            await using var connection = new SqlConnection(connectionString)
            {
                RetryLogicProvider = connectionRetryProvider,
            };
            await connection.OpenAsync();

            await using var command = new SqlCommand($"SELECT * FROM {MissingTable}", connection)
            {
                CommandTimeout = CommandTimeoutSeconds,
                RetryLogicProvider = commandRetryProvider,
            };
            await command.ExecuteScalarAsync();

            Console.WriteLine($"  actual:   {connectionRetryCount} connection retries, {commandRetryCount} command retries, succeeded");
        }
        catch (AggregateException ex) // catches if all attempts failed
        {
            // Every attempt failed with a retryable error. One inner exception per attempt.
            // A catch (SqlException) block alone would not see this.
            Console.WriteLine($"  actual:   {connectionRetryCount} connection retries, {commandRetryCount} command retries, AggregateException with {ex.InnerExceptions.Count} attempts");
        }
        catch (SqlException ex)
        {
            // Not retried: the error is not in TransientErrors, AuthorizedSqlCondition rejected the
            // command, or the command is inside a transaction.
            Console.WriteLine($"  actual:   {connectionRetryCount} connection retries, {commandRetryCount} command retries, SqlException {ex.Number}: {ex.Message}");
        }
    }

    // Scenario 2 — NO RETRY.
    // Divide by zero (8134) is not in TransientErrors, so the SqlException is thrown immediately.
    private static async Task CommandRetry_SkipsNonTransientError(string connectionString)
    {
        var commandRetryProvider = CreateCommandRetryProvider();

        await RunScenarioAsync(
            "2. Command retry skipped: error not in TransientErrors (8134)",
            expected: "0 retries, SqlException",
            commandRetryProvider,
            async () =>
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand("SELECT 1 / 0", connection)
                {
                    CommandTimeout = CommandTimeoutSeconds,
                    RetryLogicProvider = commandRetryProvider,
                };
                await command.ExecuteScalarAsync();
            });
    }

    // Runs one scenario and prints each retry, the retry count and what was thrown.
    // The Retrying event is the only hook SqlClient gives for logging retries.
    private static async Task RunScenarioAsync(
        string title,
        string expected,
        SqlRetryLogicBaseProvider watchedProvider,
        Func<Task> scenario)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine($"  expected: {expected}");

        int retryCount = 0;
        watchedProvider.Retrying += (_, e) =>
        {
            retryCount++;
            var lastError = e.Exceptions[e.Exceptions.Count - 1];
            Console.WriteLine($"  retry #{e.RetryCount} in {e.Delay.TotalSeconds:F1}s after: {lastError.Message}");
        };

        try
        {
            await scenario();
            Console.WriteLine($"  actual:   {retryCount} retries, succeeded");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  actual:   {retryCount} retries, {ex.GetType().Name}: {ex.Message}");
        }
    }
}
