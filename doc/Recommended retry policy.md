# Recommended retry policy

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
