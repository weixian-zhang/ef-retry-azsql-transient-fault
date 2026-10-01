using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace RetryTests;

/// <summary>
/// Causes a real network failure against Azure SQL by blocking outbound SQL ports on this machine
/// with a Windows Firewall rule, then checks that EF retries the query.
///
/// Runs only on Windows, elevated, with ConnectionString pointing at a reachable Azure SQL database.
/// Elsewhere, leave it out: dotnet test --filter Category!=OutboundBlock
/// If a run is killed before cleanup, remove the rule by hand:
///   Remove-NetFirewallRule -DisplayName "RetryTest-BlockAzureSql"
/// </summary>
[Trait("Category", "OutboundBlock")]
public sealed class Block_Outbound_Internet_Transient_Error_Tests
{
    // Fake: replace <server> and <database> with the real Azure SQL database.
    // Pooling=False makes every retry open a new connection; Connect Timeout=5 keeps the blocked run short.
    private const string ConnectionString =
        "Server=tcp:<server>.database.windows.net,1433;Database=<database>;" +
        "Authentication=Active Directory Managed Identity;Encrypt=True;Pooling=False;Connect Timeout=5;";

    [Fact]
    public async Task Select_is_retried_by_EF_while_outbound_network_is_blocked()
    {
        await using var db = new OrderDbContext(ConnectionString);

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
}
