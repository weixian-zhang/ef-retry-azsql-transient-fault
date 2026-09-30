using System;
using System.Threading.Tasks;

namespace SqlClientRetryReview;

public static class Program
{
    private const string ConnectionStringVariable = "SQL_CONNECTION_STRING";

    public static async Task Main()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set the {ConnectionStringVariable} environment variable to a reachable SQL Server database.");
        }

        await SqlRetryScenarios.RunAllAsync(connectionString);
    }
}
