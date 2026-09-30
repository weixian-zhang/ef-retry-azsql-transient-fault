using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace RetryTests;

/// <summary>
/// Causes a real network failure against Azure SQL by blocking outbound SQL ports on this machine
/// with a Windows Firewall rule, then checks that EF retries the query.
///
/// Runs only on Windows, elevated, with SQL_CONNECTION_STRING set to a reachable Azure SQL database.
/// Elsewhere, leave it out: dotnet test --filter Category!=OutboundBlock
/// If a run is killed before cleanup, remove the rule by hand:
///   Remove-NetFirewallRule -DisplayName "RetryTest-BlockAzureSql"
/// </summary>
[Trait("Category", "OutboundBlock")]
public sealed class Block_Outbound_Internet_Transient_Error_Tests
{
    private const string ConnectionStringVariable = "SQL_CONNECTION_STRING";

    [Fact]
    public async Task Select_is_retried_by_EF_while_outbound_network_is_blocked()
    {
        await using var db = new OrderDbContext(RealConnectionString());

        // Proves the database is reachable before the block.
        await db.Database.ExecuteSqlRawAsync("SELECT 1");

        SqlPortFirewall.Block();
        try
        {
            // EF throws RetryLimitExceededException only when ShouldRetryOn accepted the error on
            // every attempt. A SqlException here means ShouldRetryOn rejected it.
            await Assert.ThrowsAsync<RetryLimitExceededException>(() => db.Database.ExecuteSqlRawAsync("SELECT 1"));
        }
        finally
        {
            SqlPortFirewall.Allow();
        }
    }

    private static string RealConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set the {ConnectionStringVariable} environment variable to a reachable Azure SQL database.");
        }

        return new SqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,    // every retry opens a new connection instead of reusing a pooled one
            ConnectTimeout = 5, // seconds per attempt, keeps the blocked run short
        }.ConnectionString;
    }
}
