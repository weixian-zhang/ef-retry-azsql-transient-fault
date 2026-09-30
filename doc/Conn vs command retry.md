# Connection vs command retry

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
