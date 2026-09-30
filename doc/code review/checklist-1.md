| Area | What to Look For | Good ✅ | Red Flag 🚨 |
|--------|----------------|----------|------------|
| Driver | SQL Client library | `Microsoft.Data.SqlClient` | `System.Data.SqlClient` |
| Connection Retry | Retry when opening connection | `connection.RetryLogicProvider` configured | No retry configured |
| Command Retry | Retry query execution | Enabled only for safe/idempotent commands | Retry every command blindly |
| Retry Strategy | Retry implementation | Exponential Backoff | Infinite retry / immediate retry |
| Retry Count | Number of retries | 3-5 retries | Unlimited retries |
| Connection Retry Scope | What it protects | `OpenAsync()` | Assumes commands are retried too |
| Command Retry Scope | What it protects | `ExecuteReader/Scalar/NonQuery` | Assumes connection open is retried |
| Transactions | Retry behavior | Retry entire transaction as a unit | Retry individual commands |
| Transaction + Command Retry | Active transaction handling | Rollback and replay all commands | `command.RetryLogicProvider` for transactional writes |
| Idempotency | Duplicate protection | Unique keys, RequestId, BusinessId | Duplicate records possible after retry |
| Connection Timeout | SQL connection establishment | 15-30 seconds | Extremely high values |
| Command Timeout | Query execution timeout | 30-120 seconds | `CommandTimeout = 0` |
| Connection Factory | Centralized connection creation | Factory sets retry provider automatically | Repeated retry config everywhere |
| Connection Pooling | Connection lifecycle | Create/dispose per operation | Static/shared `SqlConnection` |
| Resource Disposal | Cleanup | `await using` | Connections not disposed |
| Authentication | Azure SQL auth | Managed Identity / Entra ID | Hardcoded password |
| SQL Safety | Query protection | Parameterized SQL | String concatenated SQL |
| Azure SQL Redirect | Redirect support | Modern SqlClient + network configured | Assume redirect works without validation |
| Azure SQL Redirect Ports | Network requirements | Port 1433 and 11000-11999 allowed | Firewall blocks ports |
| Logging | Retry visibility | Retries logged | No retry telemetry |
| Async Usage | Database calls | `OpenAsync`, `ExecuteXXXAsync` | Synchronous calls everywhere |

## Retry-Specific Review Questions

| Question | Expected Answer |
|-----------|----------------|
| Is `Microsoft.Data.SqlClient` used? | Yes |
| Is connection retry enabled? | Yes |
| Is exponential backoff used? | Yes |
| Is retry count bounded? | Yes |
| Is command retry used only when safe? | Yes |
| Are transactions retried as a whole? | Yes |
| Are duplicate writes prevented? | Yes |
| Are retries logged? | Yes |

## Transaction Rules

| Scenario | Recommended |
|----------|------------|
| SELECT outside transaction | Command retry OK |
| Stored procedure read-only | Command retry OK |
| INSERT in transaction | Retry whole transaction |
| UPDATE in transaction | Retry whole transaction |
| DELETE in transaction | Retry whole transaction |
| Multiple commands + COMMIT | Retry whole transaction |
| COMMIT failure | Replay whole transaction with idempotency protection |

## Recommended Retry Configuration

Connection Retry:

- Exponential Backoff
- 5 retries
- 2-second base delay
- 30-second max delay

Example:

- Attempt 1
- Wait ~2s
- Attempt 2
- Wait ~4s
- Attempt 3
- Wait ~8s
- Attempt 4
- Wait ~16s
- Attempt 5
- Fail

## 30-Second Code Review Summary

✅ `Microsoft.Data.SqlClient`

✅ Connection retry enabled

✅ Exponential backoff

✅ Bounded retry count

✅ Retry whole transaction, not individual commands

✅ Idempotent writes

✅ Connection pooling used correctly

✅ Managed Identity / Entra ID

✅ Parameterized SQL

✅ Proper timeouts

✅ Azure SQL Redirect network requirements validated

🚨 Biggest Red Flags

- `System.Data.SqlClient`
- No retry policy
- Retry-all-exceptions
- Command retry inside transactional writes
- Static/shared `SqlConnection`
- Hardcoded credentials
- SQL string concatenation
- `CommandTimeout = 0`
- Assuming Azure SQL Redirect works without network validation