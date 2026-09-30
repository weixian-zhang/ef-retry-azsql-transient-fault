using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlClientRetryReview;

/// <summary>
/// Connection and command retry for a stored procedure call. The providers are defined here on
/// purpose (not reused from SqlRetryScenarios) so this file stands alone.
/// </summary>
public static class StoredProcedureRetryScenario
{
    // Made-up read-only procedure. It does not exist, so the call fails with error 2812.
    private const string StoredProcedureName = "dbo.GetOrdersByCustomer";

    // Error 2812 "Could not find stored procedure" is NOT transient in real life. It is in the
    // list only so that calling the made-up procedure triggers the retry path on demand.
    private const int CouldNotFindStoredProcedureError = 2812;

    // 1 initial attempt + 4 retries, 2s base delay, no single delay longer than 30s.
    private const int NumberOfTries = 5;
    private static readonly TimeSpan DeltaTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxTimeInterval = TimeSpan.FromSeconds(30);
    private const int CommandTimeoutSeconds = 30;

    public static async Task RunAsync(string connectionString)
    {
        Console.WriteLine();
        Console.WriteLine($"5. Stored procedure {StoredProcedureName} with connection and command retry");
        Console.WriteLine($"  expected: 0 connection retries, {NumberOfTries - 1} command retries, then AggregateException");

        var connectionProvider = CreateConnectionRetryProvider();
        var commandProvider = CreateCommandRetryProvider();
        int connectionRetryCount = 0;
        int commandRetryCount = 0;
        connectionProvider.Retrying += (_, e) =>
        {
            connectionRetryCount++;
            Console.WriteLine($"  connection retry #{e.RetryCount} in {e.Delay.TotalSeconds:F1}s");
        };
        commandProvider.Retrying += (_, e) =>
        {
            commandRetryCount++;
            Console.WriteLine($"  command retry #{e.RetryCount} in {e.Delay.TotalSeconds:F1}s");
        };

        try
        {
            await using var connection = new SqlConnection(connectionString)
            {
                RetryLogicProvider = connectionProvider,
            };
            await connection.OpenAsync();

            await using var command = new SqlCommand(StoredProcedureName, connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = CommandTimeoutSeconds,
                RetryLogicProvider = commandProvider,
            };
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = 42;

            // Only ExecuteReaderAsync is retried. Errors while reading rows are not.
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                Console.WriteLine($"  order {reader.GetInt32(0)}");
            }

            Console.WriteLine($"  actual:   {connectionRetryCount} connection retries, {commandRetryCount} command retries, succeeded");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  actual:   {connectionRetryCount} connection retries, {commandRetryCount} command retries, {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Retries SqlConnection.Open/OpenAsync on SqlClient's built-in transient list
    /// (TransientErrors left null).
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
    /// Retries SqlCommand.Execute* for read-only stored procedures.
    /// </summary>
    public static SqlRetryLogicBaseProvider CreateCommandRetryProvider()
    {
        var commandRetryOptions = new SqlRetryLogicOption
        {
            NumberOfTries = NumberOfTries,
            DeltaTime = DeltaTime,
            MaxTimeInterval = MaxTimeInterval,
            // *optional, Replaces the built-in list, so the usual transient errors are listed again.
            TransientErrors = new[]
            {
                1205,  // deadlock victim
                40197, // Azure SQL failover
                40501, // Azure SQL service busy
                40613, // database not currently available
                CouldNotFindStoredProcedureError, // demo only, see above
            },
            // For a stored procedure, CommandText is the procedure name, not SQL, so a
            // StartsWith("SELECT") check would never match. Allow only procedures named as reads.
            AuthorizedSqlCondition = commandText =>
                commandText.StartsWith("dbo.Get", StringComparison.OrdinalIgnoreCase),
        };

        return SqlConfigurableRetryFactory.CreateExponentialRetryProvider(commandRetryOptions);
    }
}
