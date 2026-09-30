using System.Threading.Tasks;
using Xunit;

namespace RetryTests;

/// <summary>
/// Simulates the errors a client sees during an Azure SQL failover, locally and with no database.
/// A connection interceptor throws the chosen SqlException at the exact point EF opens the SQL
/// connection, and the tests check how EF's execution strategy reacts.
///
/// For reference, SqlClient 5.2.2's built-in transient error list:
///
///   // Locking
///   1204,  // lock resources exhausted
///   1205,  // deadlock victim
///   1222,  // lock request timeout
///
///   // Resource limits and throttling
///   10928, // resource limit reached (workers or sessions)
///   10929, // server too busy (above minimum guarantee)
///   40501, // Azure SQL service busy (throttling)
///   49918, // not enough resources to process request
///   49919, // too many create/update operations for subscription
///   49920, // too many operations for subscription
///
///   // Database availability during login
///   4060,  // cannot open database (failover, restore, scaling, auto-pause)
///   4221,  // login to readable secondary failed (replica transitioning)
///
///   // Azure SQL failover
///   40143, // Azure SQL service error
///   40197, // Azure SQL failover
///   40540, // Azure SQL failover subcode
///   40613, // database not currently available
///
///   // Transport during login
///   233,   // connection initialization failed before login
///   997,   // overlapped I/O in progress
///   10060, // connection attempt timed out
/// </summary>
public sealed class Local_No_DB_Transient_Error_Tests
{
    [Theory]
    // Locking
    [InlineData(1204)]  // lock resources exhausted
    [InlineData(1205)]  // deadlock victim
    [InlineData(1222)]  // lock request timeout
    // Resource limits and throttling
    [InlineData(10928)] // resource limit reached (workers or sessions)
    [InlineData(10929)] // server too busy (above minimum guarantee)
    [InlineData(40501)] // Azure SQL service busy (throttling)
    [InlineData(49918)] // not enough resources to process request
    [InlineData(49919)] // too many create/update operations for subscription
    [InlineData(49920)] // too many operations for subscription
    // Database availability during login
    [InlineData(4060)]  // cannot open database (failover, restore, scaling, auto-pause)
    [InlineData(4221)]  // login to readable secondary failed (replica transitioning)
    // Azure SQL failover
    [InlineData(40143)] // Azure SQL service error
    [InlineData(40197)] // Azure SQL failover
    [InlineData(40540)] // Azure SQL failover subcode
    [InlineData(40613)] // database not currently available
    // Transport
    [InlineData(233)]   // connection initialization failed before login
    [InlineData(997)]   // overlapped I/O in progress during login
    [InlineData(10060)] // connection attempt timed out
    [InlineData(64)]    // network name no longer available
    [InlineData(10053)] // connection aborted
    [InlineData(10054)] // connection reset by remote host
    public async Task Transient_error_is_retried_until_the_connection_opens(int errorNumber)
    {
        var interceptor = new FailingConnectionInterceptor(errorNumber, failuresBeforeSuccess: 2);
        await using var db = new OrderDbContext(interceptor);

        await db.OpenConnectionWithRetryAsync();

        Assert.Equal(3, interceptor.OpenAttempts); // 2 failures + 1 success
    }
}
