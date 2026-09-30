# .NET 6 + Microsoft.Data.SqlClient Azure SQL Code Review (Simple Checklist)

## Driver

✅ Use:

- `Microsoft.Data.SqlClient`

🚨 Avoid:

- `System.Data.SqlClient`

---

## Connection Retry

✅ Look for:

```csharp
connection.RetryLogicProvider = retryProvider;


Purpose:

Retries OpenAsync()
Handles Azure SQL failover
Handles transient network issues
Handles temporary SQL availability issues

Questions:

Is connection retry enabled?
Is retry provider assigned before OpenAsync()?
Command Retry

✅ Look for:

command.RetryLogicProvider = retryProvider;


Purpose:

Retries ExecuteReaderAsync()
Retries ExecuteScalarAsync()
Retries ExecuteNonQueryAsync()

Questions:

Is command retry intentionally enabled?
Is the command safe to execute more than once?

✅ Safe examples:

SELECT
Read-only stored procedures

⚠️ Review carefully:

INSERT
UPDATE
DELETE
Connection Retry vs Command Retry
Feature	ProtectsConnection Retry	OpenAsync()
Command Retry	ExecuteXXXAsync()

They are independent.

Common mistake:

Connection retry enabled

Assume command execution is retried

❌ Wrong

Transactions

✅ Look for:

BeginTransaction()


Question:

Is the whole transaction retried?

✅ Correct

Begin Transaction
Command 1
Command 2
Commit

Failure

Rollback

Retry everything


🚨 Incorrect

Begin Transaction
Command 1 succeeds
Command 2 fails

Retry Command 2 only

Command Retry Inside Transactions

🚨 Important

Microsoft built-in command retry provider does NOT retry commands inside active transactions.

If transaction fails:

✅ Correct

Rollback

Create new transaction

Replay all commands

Commit


🚨 Incorrect

Retry failed command only

Idempotency

Question:

Can retry create duplicate business records?

✅ Good

RequestId
BusinessId
OrderId
Unique constraint

Examples:

OrderId PRIMARY KEY

RequestId UNIQUE


Required especially when:

INSERT
UPDATE
Transaction replay
Recommended Retry Settings

✅ Recommended baseline

NumberOfTries = 5
DeltaTime = 2 seconds
MaxTimeInterval = 30 seconds


✅ Use:

CreateExponentialRetryProvider(...)


Questions:

Is retry bounded?
Is exponential backoff used?

🚨 Avoid:

Infinite retries
Immediate retry loops
Timeouts

✅ Connection

Connect Timeout=30


✅ Command

command.CommandTimeout = 30;


Questions:

Are timeouts explicitly set?
Are values reasonable?

🚨 Avoid:

CommandTimeout = 0;

Managed Identity Authentication

✅ Preferred

Authentication=Active Directory Default


Questions:

Using Managed Identity?
Using Entra ID?

🚨 Avoid:

User ID=...
Password=...


for Azure-hosted applications.

Connection Factory

✅ Good pattern

Connection factory:

Creates SqlConnection
Configures retry provider
Returns configured connection

Benefits:

Consistent configuration
Easier maintenance
Easier code review
Connection Pooling

✅ Good

await using var connection =
    new SqlConnection(...);


Create and dispose per operation.

ADO.NET pooling handles reuse.

🚨 Avoid

static SqlConnection

singleton SqlConnection

SQL Safety

✅ Use parameters

cmd.Parameters.Add(...)


🚨 Avoid

"SELECT * FROM User WHERE Id=" + userInput


Questions:

SQL Injection?
Parameterized queries?
Azure SQL Redirect

Questions:

Using Microsoft.Data.SqlClient?
Port 1433 allowed?
Ports 11000-11999 allowed?
Redirect mode tested?

Remember:

Driver supports Redirect


does NOT mean:

Network supports Redirect

Quick Pass Checklist

✅ Microsoft.Data.SqlClient

✅ Connection RetryLogicProvider configured

✅ Command RetryLogicProvider reviewed and justified

✅ Exponential Retry Provider

✅ 5 retries / 2-second base / 30-second max

✅ Retry only transient failures

✅ Whole transaction retried

✅ No command retry for transactional writes

✅ INSERT/UPDATE idempotent

✅ Managed Identity / Entra ID

✅ Parameterized SQL

✅ Proper timeouts

✅ Connections disposed correctly

✅ Azure SQL Redirect requirements validated

Biggest Red Flags

🚨 System.Data.SqlClient

🚨 No retry configuration

🚨 Infinite retries

🚨 Retry all exceptions

🚨 Command retry on transactional INSERT/UPDATE/DELETE

🚨 No idempotency for writes

🚨 Static/shared SqlConnection

🚨 Hardcoded passwords

🚨 SQL string concatenation

🚨 CommandTimeout = 0

🚨 Assuming Redirect works without validating network/firewall