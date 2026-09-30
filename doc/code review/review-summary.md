| Checklist Item | What to Look For | Recommended |
|----------------|------------------|-------------|
| ✅ Microsoft.Data.SqlClient | Modern SQL Server driver with Azure SQL support | Use `Microsoft.Data.SqlClient` (avoid `System.Data.SqlClient`) |
| ✅ Connection RetryLogicProvider configured | Retries connection open failures (`OpenAsync`) | `connection.RetryLogicProvider = provider` |
| ✅ Command RetryLogicProvider reviewed and justified | Retries command execution failures (`ExecuteXXXAsync`) | Use mainly for SELECT/read-only/idempotent operations |
| ✅ Exponential Retry Provider | Retry delay increases after each retry | `CreateExponentialRetryProvider(...)` |
| ✅ Retry Settings | Production baseline | 5 tries, 2-second base delay, 30-second max delay |
| ✅ Retry only transient failures | Retry only temporary errors such as failover/network/throttling | Use built-in SqlClient transient error detection |
| ✅ Whole transaction retried | Transaction treated as one unit of work | Rollback + replay all commands + Commit |
| ✅ No command retry for transactional writes | Prevent partial transaction replay | Do not rely on `command.RetryLogicProvider` inside write transactions |
| ✅ INSERT/UPDATE idempotent | Re-running operation should not create duplicate business records | RequestId, BusinessKey, OrderId unique constraint |
| ✅ Managed Identity / Entra ID | Passwordless Azure SQL authentication | `Authentication=Active Directory Default` |
| ✅ Parameterized SQL | Protects against SQL injection and type issues | `cmd.Parameters.Add(...)` |
| ✅ Proper Connection Timeout | Maximum wait to establish SQL connection | `Connect Timeout=30` seconds |
| ✅ Proper Command Timeout | Maximum wait for query execution | `CommandTimeout = 30` seconds (60-120 secs for reporting workloads) |
| ✅ Connections disposed correctly | Returns connections to pool properly | `await using var connection = ...` |
| ✅ Connection Pooling Used Properly | Create/dispose per operation; let ADO.NET manage pool | Avoid static/singleton `SqlConnection` |
| ✅ Azure SQL Redirect requirements validated | Direct database routing supported and tested | Port 1433 + ports 11000-11999 allowed |
| ✅ Retry Logging | Retry attempts visible in monitoring/logs | Log retry attempt count, error and delay |
| ✅ Async APIs Used | Non-blocking database operations | `OpenAsync`, `ExecuteReaderAsync`, etc. |
| ✅ Secrets Management | No credentials embedded in source code | Use Managed Identity, Key Vault, App Configuration |
| ✅ Command Retry Usage Reviewed | Safe operation if executed more than once | SELECT ✔️, Transactional INSERT/UPDATE ❌ |

## Recommended Retry Policy

```csharp
var retryOptions = new SqlRetryLogicOption
{
    NumberOfTries = 5,
    DeltaTime = TimeSpan.FromSeconds(2),
    MaxTimeInterval = TimeSpan.FromSeconds(30)
};

var retryProvider =
    SqlConfigurableRetryFactory.CreateExponentialRetryProvider(
        retryOptions);

Retry Schedule (Approximate)
Attempt	Delay1	Immediate
2	~2 sec
3	~4 sec
4	~8 sec
5	~16 sec
Fail	Throw Exception
Command Retry Quick Rule
Scenario	Command Retry?SELECT	✅ Yes
Read-only Stored Procedure	✅ Yes
INSERT outside transaction with idempotency	⚠️ Maybe
UPDATE outside transaction with idempotency	⚠️ Maybe
DELETE outside transaction with idempotency	⚠️ Maybe
INSERT inside transaction	❌ No
UPDATE inside transaction	❌ No
DELETE inside transaction	❌ No
Multi-command transaction	❌ Retry whole transaction instead
15-Second Code Review Summary

✅ Microsoft.Data.SqlClient

✅ Connection retry configured

✅ Command retry justified

✅ Exponential backoff

✅ 5 retries / 2s base / 30s max

✅ Only transient failures retried

✅ Whole transaction replayed

✅ INSERT/UPDATE idempotent

✅ Managed Identity

✅ Parameterized SQL

✅ Connect Timeout = 30s

✅ Command Timeout = 30s

✅ await using for disposal

✅ Azure SQL Redirect validated (1433 + 11000-11999)