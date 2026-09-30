  
  
  
  
For **.NET 6** using **Microsoft.Data.SqlClient** with built-in exponential retry logic:  
## NuGet Package  
  
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.0" />  
  
(Any 3.0+ version supports configurable retry logic.)  
## Example  
  
using Microsoft.Data.SqlClient;  
  
string connectionString =  
    "Server=tcp:myserver.database.windows.net,1433;" +  
    "Database=MyDatabase;" +  
    "Authentication=Active Directory Default;" +  
    "Encrypt=True;";  
  
// Configure retry policy  
var retryOptions = new SqlRetryLogicOption  
{  
    NumberOfTries = 5,                    // Total attempts including first try  
    DeltaTime = TimeSpan.FromSeconds(1),  // Base delay  
    MaxTimeInterval = TimeSpan.FromSeconds(30)  
};  
  
var retryProvider =  
    SqlConfigurableRetryFactory.CreateExponentialRetryProvider(retryOptions);  
  
await using var connection =  
    new SqlConnection(connectionString);  
  
// Enable connection retry  
connection.RetryLogicProvider = retryProvider;  
  
await connection.OpenAsync();  
  
await using var command = new SqlCommand(  
    "SELECT COUNT(*) FROM Users",  
    connection);  
  
// Optional: enable command retry too  
command.RetryLogicProvider = retryProvider;  
  
var count = (int)await command.ExecuteScalarAsync();  
  
Console.WriteLine($"Users: {count}");  
  
## Code Review Checks  
When reviewing this code, look for:  
  
connection.RetryLogicProvider = retryProvider;  
  
and optionally:  
  
command.RetryLogicProvider = retryProvider;  
  
If these are missing, built-in SqlClient retry is not enabled because configurable retry logic is off by default and must be explicitly assigned. It is available in Microsoft.Data.SqlClient 3.0+ and supports exponential retry providers.  
## Typical Interview / Review Question  
Is Microsoft.Data.SqlClient retry logic enabled, how many retries are configured, and does it use exponential backoff rather than retrying indefinitely?  
That's usually the key question for ADO.NET Azure SQL resiliency reviews.  
  
  
  
———  
  
It means **"try up to 5 times, and wait longer between each retry."**  
  
var retryOptions = new SqlRetryLogicOption  
{  
    NumberOfTries = 5,  
    DeltaTime = TimeSpan.FromSeconds(1),  
    MaxTimeInterval = TimeSpan.FromSeconds(30)  
};  
  
## NumberOfTries = 5  
Total attempts, **including the first attempt**.  
  
Attempt 1  <-- original attempt  
Attempt 2  <-- retry #1  
Attempt 3  <-- retry #2  
Attempt 4  <-- retry #3  
Attempt 5  <-- retry #4  
  
After the 5th attempt fails, an exception is thrown.  
## DeltaTime = 1 second  
The base delay used by the exponential retry algorithm.  
The retry provider uses this value to calculate increasing delays between retries.  
## MaxTimeInterval = 30 seconds  
No single retry delay can exceed 30 seconds.  
Even if the exponential calculation grows larger, SqlClient caps the delay at 30 seconds.  
## Rough Example  
The actual timings include some random jitter, but conceptually:  
  
Attempt 1 -> Fail  
Wait ~1 sec  
  
Attempt 2 -> Fail  
Wait ~2 sec  
  
Attempt 3 -> Fail  
Wait ~4 sec  
  
Attempt 4 -> Fail  
Wait ~8 sec  
  
Attempt 5 -> Fail  
Throw Exception  
  
## What Azure SQL Problems Does This Help With?  
This is intended for **transient failures**, such as:  
* Temporary network interruption  
* Azure SQL failover  
* Throttling  
* Brief database unavailability  
* Transport errors during connection establishment  
SqlClient has a built-in list of transient Azure SQL and SQL Server error codes that it knows are safe to retry.  
## Code Review Perspective  
When you see:  
  
SqlConfigurableRetryFactory.CreateExponentialRetryProvider(...)  
  
ask yourself:  
1. Is retry enabled at all?  
2. Is NumberOfTries reasonable (typically 3-5)?  
3. Is there exponential backoff instead of immediate retries?  
4. Is the maximum delay bounded?  
5. Is retry only for transient failures?  
If the answer is yes to all five, the retry configuration is generally reasonable for Azure SQL.  
  
———  
  
For **Azure SQL + .NET 6/8 + Microsoft.Data.SqlClient**, I'd recommend a **simple production-ready policy** like this:  
  
var retryOptions = new SqlRetryLogicOption  
{  
    NumberOfTries = 5,  
    DeltaTime = TimeSpan.FromSeconds(2),  
    MaxTimeInterval = TimeSpan.FromSeconds(30)  
};  
  
var retryProvider =  
    SqlConfigurableRetryFactory.CreateExponentialRetryProvider(retryOptions);  
  
connection.RetryLogicProvider = retryProvider;  
command.RetryLogicProvider = retryProvider;  
  
## Why these values?  
* **5 tries**: Handles most Azure SQL transient failures without delaying users too long.  
* **2-second base delay**: Prevents immediate hammering of a service recovering from failover or throttling.  
* **30-second max delay**: Caps retry wait times during larger incidents.  
* **Exponential backoff with jitter**: Reduces retry storms when many application instances experience the same transient fault.  
## Typical Retry Timeline  
Approximately:  
  
Attempt 1  
Wait ~2s  
  
Attempt 2  
Wait ~4s  
  
Attempt 3  
Wait ~8s  
  
Attempt 4  
Wait ~16s  
  
Attempt 5  
Fail  
  
Actual intervals include random jitter added by SqlClient.  
## Additional Best Practices  
Configure reasonable timeouts:  
  
var builder = new SqlConnectionStringBuilder(connectionString)  
{  
    ConnectTimeout = 30  
};  
  
And for commands:  
  
command.CommandTimeout = 30;  
  
Use CancellationToken for all async operations so callers can cancel work before timeouts occur.  
## Code Review Questions  
When reviewing Azure SQL access code, I normally ask:  
* Is Microsoft.Data.SqlClient being used?  
* Is retry enabled on the connection?  
* Is command retry required and enabled?  
* Is exponential backoff being used?  
* Are retries bounded (not infinite)?  
* Are only transient failures retried?  
* Are writes idempotent?  
* Are transactions retried as a whole instead of individual statements?  
* Are ConnectTimeout and CommandTimeout configured?  
* Is Managed Identity / Entra ID used where possible?  
## My Recommended Baseline  
For most enterprise Azure SQL applications:  
  
NumberOfTries = 5  
DeltaTime = 2 seconds  
MaxTimeInterval = 30 seconds  
  
If I saw that configuration during a code review, I would generally consider the retry policy to be **good enough and production-ready**unless there were special business requirements.  
  
  
———  
  
Good question. The **exponential backoff is not specified by SqlRetryLogicOption itself**.  
It is specified here:  
  
var retryProvider =  
    SqlConfigurableRetryFactory.CreateExponentialRetryProvider(retryOptions);  
  
The word ****Exponential**** in:  
  
CreateExponentialRetryProvider(...)  
  
is what tells SqlClient to use exponential backoff.  
There are different providers available:  
  
SqlConfigurableRetryFactory.CreateNoneRetryProvider()  
  
SqlConfigurableRetryFactory.CreateFixedRetryProvider(...)  
  
SqlConfigurableRetryFactory.CreateIncrementalRetryProvider(...)  
  
SqlConfigurableRetryFactory.CreateExponentialRetryProvider(...)  
  
Each provider interprets the same SqlRetryLogicOption differently.  
## Fixed Retry  
  
var provider =  
    SqlConfigurableRetryFactory.CreateFixedRetryProvider(options);  
  
Example:  
  
Retry 1 -> 2 sec  
Retry 2 -> 2 sec  
Retry 3 -> 2 sec  
Retry 4 -> 2 sec  
  
## Incremental Retry  
  
var provider =  
    SqlConfigurableRetryFactory.CreateIncrementalRetryProvider(options);  
  
Example:  
  
Retry 1 -> 2 sec  
Retry 2 -> 4 sec  
Retry 3 -> 6 sec  
Retry 4 -> 8 sec  
  
## Exponential Retry  
  
var provider =  
    SqlConfigurableRetryFactory.CreateExponentialRetryProvider(options);  
  
Example:  
  
Retry 1 -> 2 sec  
Retry 2 -> 4 sec  
Retry 3 -> 8 sec  
Retry 4 -> 16 sec  
  
The built-in providers also add some random jitter to avoid retry storms.  
## Code Review Tip  
For Azure SQL, the specific line I would look for is:  
  
SqlConfigurableRetryFactory.CreateExponentialRetryProvider(...)  
  
If I see:  
  
CreateFixedRetryProvider(...)  
  
I would ask:  
Why fixed delay instead of exponential backoff for transient Azure SQL failures?  
For most Azure SQL production workloads, **ExponentialRetryProvider** is generally the preferred choice because it backs off more aggressively during throttling, failovers, and temporary outages.  
  
  
———  
  
## Connection Timeout vs Command Timeout  
These are two different things.  

| Setting | What it controls | When it starts | Default |
| ------------------ | --------------------------------------------------- | --------------------------------------------------- | ---------- |
| Connection Timeout | How long to wait to establish a database connection | Open() / OpenAsync() | 15 seconds |
| Command Timeout | How long a SQL query/command can run | ExecuteReader(), ExecuteScalar(), ExecuteNonQuery() | 30 seconds |
  
## Connection Timeout  
Controls the time allowed to:  
* DNS resolution  
* Network connection  
* Authentication  
* Obtaining a connection from the pool  
Example:  
  
Server=myserver.database.windows.net;  
Database=MyDb;  
Connect Timeout=30;  
  
  
await connection.OpenAsync();  
  
If the database cannot be reached within 30 seconds, OpenAsync() throws an exception.  
Think of it as:  
"How long am I willing to wait to connect to SQL?"  
  
## Command Timeout  
Controls the time allowed for SQL execution after the connection is already open.  
Example:  
  
command.CommandTimeout = 60;  
  
  
await command.ExecuteReaderAsync();  
  
If the query runs longer than 60 seconds, SQL Client cancels the command and throws a timeout exception.  
Think of it as:  
"How long am I willing to wait for this query to finish?"  
  
## Example Timeline  
  
Application starts  
  
Open SQL Connection  
├─ Wait 10 seconds  
└─ Connected ✅  
  
Execute Query  
├─ Query runs 45 seconds  
└─ Query completed ✅  
  
If:  
  
Connect Timeout = 15  
Command Timeout = 30  
  
Then:  
* Connection succeeds because 10 < 15 seconds  
* Query fails because 45 > 30 seconds  
  
## Code Review Questions  
**Connection Timeout**  
Ask:  
* Is Connect Timeout explicitly configured?  
* Is it reasonable (typically 15–30 seconds)?  
* Is the application hiding slow networking issues by setting it to very large values?  
Red flag:  
  
Connect Timeout=300  
  
(Waiting 5 minutes for a connection usually indicates a problem.)  
  
**Command Timeout**  
Ask:  
* Is CommandTimeout configured appropriately?  
* Are long-running queries optimized instead?  
* Is timeout set to unlimited?  
Red flag:  
  
command.CommandTimeout = 0;  
  
A value of 0 means no timeout limit and can allow queries to hang indefinitely.  
  
## Recommended Azure SQL Defaults  
For most enterprise APIs:  
  
Connect Timeout = 30  
Command Timeout = 30  
  
If running reports or analytics:  
  
Connect Timeout = 30  
Command Timeout = 120  
  
## Simple Memory Aid  
* **Connection Timeout** = "Can I reach SQL?"  
* **Command Timeout** = "Can SQL finish the query?"  
  
  
———  
  
This is actually a very good code review question because **connection retry and command retry solve different problems**.  
## Connection Retry  
Applied to:  
  
connection.RetryLogicProvider = retryProvider;  
  
Protects:  
  
await connection.OpenAsync();  
  
Examples:  
* Azure SQL failover during login  
* Temporary network outage  
* Database temporarily unavailable  
* Authentication connection failures  
* Connection establishment timeout  
  
App  
 └── Open Connection  
      ❌ Fail  
      🔄 Retry Open Connection  
      ✅ Connected  
  
Connection retry only helps **before the connection is established**.  
  
## Command Retry  
Applied to:  
  
command.RetryLogicProvider = retryProvider;  
  
Protects:  
  
await command.ExecuteReaderAsync();  
await command.ExecuteScalarAsync();  
await command.ExecuteNonQueryAsync();  
  
Examples:  
* Azure SQL transient error during query execution  
* Throttling (40501)  
* SQL failover during execution  
* Transport interruption after connection established  
  
Open Connection ✅  
  
Execute Query  
    ❌ Fail  
    🔄 Retry Query  
    ✅ Success  
  
Command retry only helps **after the connection is already open**.  
  
## Why Connection Retry Is Not Enough  
A common misunderstanding:  
  
connection.RetryLogicProvider = retryProvider;  
  
Developers think:  
Great, all SQL operations are protected.  
Actually:  
  
OpenAsync() ✅ protected  
  
ExecuteReaderAsync() ❌ not protected  
ExecuteScalarAsync() ❌ not protected  
ExecuteNonQueryAsync() ❌ not protected  
  
Because connection and command retry providers are independent.  
  
## Why Command Retry Is Not Enough  
Suppose:  
  
command.RetryLogicProvider = retryProvider;  
  
but:  
  
await connection.OpenAsync();  
  
fails due to temporary Azure SQL failover.  
The command is never reached.  
  
OpenAsync()  
   ❌ Fail  
  
No retry occurs.  
  
## Best Practice  
For Azure SQL:  
  
connection.RetryLogicProvider = retryProvider;  
command.RetryLogicProvider = retryProvider;  
  
Protect both.  
  
Open Connection  
    🔄 Retry if needed  
  
Execute Command  
    🔄 Retry if needed  
  
This provides resilience during both:  
* Connection establishment  
* Command execution  
  
## Important Caveat: Transactions  
Be extremely careful with command retry when transactions are involved.  
Bad:  
  
BeginTransaction()  
  
INSERT Order  
  
Connection drops  
  
Retry INSERT  
  
You may not know whether the first INSERT succeeded.  
This can create duplicate business data. Microsoft explicitly advises retrying the **entire transaction as a unit**, not individual commands inside an active transaction.  
  
## What I'd Expect in a Code Review  
## Green ✅  
  
connection.RetryLogicProvider = retryProvider;  
command.RetryLogicProvider = retryProvider;  
  
with:  
  
CreateExponentialRetryProvider(...)  
  
and reasonable retry counts.  
## Yellow ⚠️  
  
connection.RetryLogicProvider = retryProvider;  
  
only.  
Ask:  
Are command execution failures intentionally not retried?  
## Red 🚨  
No retry provider at all.  
or  
Custom retry loop:  
  
catch(Exception)  
{  
    retry();  
}  
  
which retries every exception indiscriminately.  
  
## My Recommendation  
For a typical Azure SQL application:  
  
connection.RetryLogicProvider = retryProvider;  
command.RetryLogicProvider = retryProvider;  
  
Use:  
  
SqlConfigurableRetryFactory.CreateExponentialRetryProvider(...)  
  
with:  
  
NumberOfTries = 5  
DeltaTime = 2 seconds  
MaxTimeInterval = 30 seconds  
  
and ensure transaction-based business operations are retried as a whole, not command-by-command.  
  
  
———  
  
Yes. A small ****SqlConnectionFactory**** is a good approach because it ensures every connection gets the same connection string and retry policy.  
The retry provider must be assigned **before** calling OpenAsync().  
## Simple factory class  
  
using Microsoft.Data.SqlClient;  
  
public sealed class SqlConnectionFactory  
{  
    private readonly string _connectionString;  
    private readonly SqlRetryLogicBaseProvider _connectionRetryProvider;  
  
    public SqlConnectionFactory(string connectionString)  
    {  
        _connectionString = connectionString;  
  
        var retryOptions = new SqlRetryLogicOption  
        {  
            NumberOfTries = 5, // One initial attempt + four retries  
            DeltaTime = TimeSpan.FromSeconds(2),  
            MaxTimeInterval = TimeSpan.FromSeconds(30)  
        };  
  
        _connectionRetryProvider =  
            SqlConfigurableRetryFactory  
                .CreateExponentialRetryProvider(retryOptions);  
    }  
  
    public SqlConnection CreateConnection()  
    {  
        return new SqlConnection(_connectionString)  
        {  
            RetryLogicProvider = _connectionRetryProvider  
        };  
    }  
}  
  
## Usage  
  
var factory = new SqlConnectionFactory(connectionString);  
  
await using var connection = factory.CreateConnection();  
await connection.OpenAsync();  
  
await using var command = new SqlCommand(  
    "SELECT Id, Name FROM Users",  
    connection);  
  
await using var reader = await command.ExecuteReaderAsync();  
  
while (await reader.ReadAsync())  
{  
    Console.WriteLine(reader["Name"]);  
}  
  
The caller should still dispose each returned connection. ADO.NET connection pooling is enabled by default, so disposing a connection normally returns the underlying physical connection to the pool instead of wasting it.  
## Important: This only configures connection retry  
This line:  
  
connection.RetryLogicProvider = _connectionRetryProvider;  
  
retries **opening the connection**. It does not automatically apply the provider to commands. Connection and command retry policies are configured separately.  
If you also want a command factory, keep it separate so command retry is an explicit decision:  
  
public sealed class SqlCommandFactory  
{  
    private readonly SqlRetryLogicBaseProvider _commandRetryProvider;  
  
    public SqlCommandFactory()  
    {  
        var retryOptions = new SqlRetryLogicOption  
        {  
            NumberOfTries = 5,  
            DeltaTime = TimeSpan.FromSeconds(2),  
            MaxTimeInterval = TimeSpan.FromSeconds(30),  
  
            // Only allow retry for commands considered safe to repeat.  
            AuthorizedSqlCondition = commandText =>  
                commandText.TrimStart()  
                           .StartsWith(  
                               "SELECT",  
                               StringComparison.OrdinalIgnoreCase)  
        };  
  
        _commandRetryProvider =  
            SqlConfigurableRetryFactory  
                .CreateExponentialRetryProvider(retryOptions);  
    }  
  
    public SqlCommand CreateCommand(  
        string sql,  
        SqlConnection connection)  
    {  
        return new SqlCommand(sql, connection)  
        {  
            RetryLogicProvider = _commandRetryProvider,  
            CommandTimeout = 30  
        };  
    }  
}  
  
Usage:  
  
await using var connection = connectionFactory.CreateConnection();  
await connection.OpenAsync();  
  
await using var command =  
    commandFactory.CreateCommand(  
        "SELECT Id, Name FROM Users",  
        connection);  
  
await using var reader = await command.ExecuteReaderAsync();  
  
## Recommendation  
* **Yes:** Centralize connection creation and connection retry.  
* **Do not:** Return one shared/static SqlConnection.  
* **Create and dispose:** A new SqlConnection object per database operation; let connection pooling handle physical connection reuse.  
* **Keep command retry explicit:** Automatically retrying writes can duplicate operations.  
* **For transactions:** Retry the entire transaction, not one command inside it. SqlClient’s built-in command provider does not retry commands on a connection with an active transaction.  
For most applications, I would implement the **connection factory first**, then add command retry only for operations that are clearly safe to repeat.  
  
  
———  
  
  
I assume you mean a simple ****SqlCommand transaction retry pattern**** using Microsoft.Data.SqlClient.  
Microsoft recommends **not assigning command retry providers to individual commands inside a transaction**. Instead, wrap the **connection, transaction, commands, and commit** in one retry loop. If a transient failure occurs, create a new connection and transaction, then replay the entire operation.  
  
using Microsoft.Data.SqlClient;  
  
public static async Task CreateOrderAsync(  
    string connectionString,  
    int orderId,  
    CancellationToken cancellationToken = default)  
{  
    const int maxAttempts = 5;  
  
    for (int attempt = 1; attempt <= maxAttempts; attempt++)  
    {  
        try  
        {  
            // Create a new connection for every attempt  
            await using var connection =  
                new SqlConnection(connectionString);  
  
            await connection.OpenAsync(cancellationToken);  
  
            await using var transaction =  
                await connection.BeginTransactionAsync(cancellationToken);  
  
            try  
            {  
                // Command 1  
                await using var orderCommand = new SqlCommand(  
                    """  
                    INSERT INTO Orders (Id, Status)  
                    VALUES (@Id, @Status)  
                    """,  
                    connection,  
                    (SqlTransaction)transaction);  
  
                orderCommand.Parameters.AddWithValue("@Id", orderId);  
                orderCommand.Parameters.AddWithValue("@Status", "Created");  
  
                await orderCommand.ExecuteNonQueryAsync(cancellationToken);  
  
                // Command 2  
                await using var auditCommand = new SqlCommand(  
                    """  
                    INSERT INTO AuditLog (OrderId, Message)  
                    VALUES (@OrderId, @Message)  
                    """,  
                    connection,  
                    (SqlTransaction)transaction);  
  
                auditCommand.Parameters.AddWithValue("@OrderId", orderId);  
                auditCommand.Parameters.AddWithValue(  
                    "@Message",  
                    "Order created");  
  
                await auditCommand.ExecuteNonQueryAsync(cancellationToken);  
  
                // Both commands succeed or fail together  
                await transaction.CommitAsync(cancellationToken);  
                return;  
            }  
            catch  
            {  
                await transaction.RollbackAsync(CancellationToken.None);  
                throw;  
            }  
        }  
        catch (SqlException ex) when (  
            IsTransient(ex) && attempt < maxAttempts)  
        {  
            // Simple exponential backoff: 2, 4, 8, 16 seconds  
            var delay = TimeSpan.FromSeconds(  
                Math.Pow(2, attempt));  
  
            Console.WriteLine(  
                $"Transaction failed. Retrying attempt " +  
                $"{attempt + 1}/{maxAttempts} in {delay.TotalSeconds}s.");  
  
            await Task.Delay(delay, cancellationToken);  
        }  
    }  
}  
  
A simple transient-error check:  
  
private static bool IsTransient(SqlException exception)  
{  
    int[] transientErrors =  
    [  
        1205,  // Deadlock  
        4060,  // Database unavailable  
        10928, // Resource limit  
        10929, // Resource limit  
        40143, // Azure SQL service error  
        40197, // Azure SQL service error/failover  
        40501, // Service busy  
        40613, // Database unavailable  
        49918,  
        49919,  
        49920  
    ];  
  
    return exception.Errors  
        .Cast<SqlError>()  
        .Any(error => transientErrors.Contains(error.Number));  
}  
  
## The important pattern  
  
Attempt 1  
├── Create new connection  
├── Begin new transaction  
├── Execute command 1  
├── Execute command 2  
└── Commit  
       ↓ transient failure  
  
Attempt 2  
├── Create new connection  
├── Begin new transaction  
├── Execute command 1 again  
├── Execute command 2 again  
└── Commit  
  
Do **not** set this inside the transaction:  
  
command.RetryLogicProvider = retryProvider;  
  
SqlClient’s built-in command retry provider does not retry commands on a connection with an active transaction. If the transaction becomes invalid, retrying only one statement could produce incorrect or partial results.  
## Critical production consideration  
Use an idempotency or unique business key, such as the orderId primary key in this example. If the connection fails while CommitAsync() is completing, the application may not know whether the transaction committed. Retrying without duplicate protection could create duplicate records.  
For production code, also:  
* Add random jitter to the retry delay.  
* Log each retry.  
* Keep retries bounded.  
* Use parameterized queries.  
* Pass CancellationToken.  
* Recreate the connection and transaction for every attempt.  
* Do not retry non-transient errors. Microsoft’s built-in exponential providers add jitter and recognize a maintained list of transient errors, but their command provider is intentionally not used within an active transaction.  
  
  
———  
  
Yes—**conceptually, you are right**:  
  
INSERT Order       ✅ succeeds  
INSERT AuditLog    ❌ transient failure  
INSERT AuditLog    🔄 command-level retry  
  
A command-level retry retries only the **AuditLog command**, not the earlier Order command.  
However, inside an active SQL transaction, Microsoft.Data.SqlClient’s built-in command retry provider intentionally **does not retry the failed command**. Microsoft recommends rolling back and retrying the entire transaction instead.  
Therefore, the actual recommended flow is:  
  
Attempt 1  
├── Begin transaction  
├── INSERT Order       ✅  
├── INSERT AuditLog    ❌  
└── Roll back everything  
    ├── Order insert rolled back  
    └── Audit insert rolled back  
  
Attempt 2  
├── Begin new transaction  
├── INSERT Order       🔄 run again  
├── INSERT AuditLog    🔄 run again  
└── Commit             ✅  
  
Because both inserts are in the same transaction, rolling back removes the successful Order insert too. The next attempt then executes both commands again as one unit.  
## Simple distinction  
* **If there is no transaction:** command retry retries only INSERT AuditLog.  
* **If there is an active transaction:** built-in command retry does not retry the individual command; the application should roll back and retry both INSERT Order and INSERT AuditLog.  
So your understanding is right about how **individual command retry generally works**, but SqlClient avoids that behavior inside a transaction because retrying only part of the transaction may be unsafe.  
  
  
