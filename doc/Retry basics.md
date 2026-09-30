# Retry basics

Built-in exponential retry for .NET 6 with Microsoft.Data.SqlClient.

## Package

```xml
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.0" />
```

Version 4.0+ works as is. On 3.x, retry is ignored unless the app first calls
`AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.EnableRetryLogic", true)`.

## Example

```csharp
var retryOptions = new SqlRetryLogicOption
{
    NumberOfTries = 5,                         // total attempts, including the first
    DeltaTime = TimeSpan.FromSeconds(1),       // base delay
    MaxTimeInterval = TimeSpan.FromSeconds(30) // cap on any single delay
};

var retryProvider = SqlConfigurableRetryFactory.CreateExponentialRetryProvider(retryOptions);

await using var connection = new SqlConnection(connectionString);
connection.RetryLogicProvider = retryProvider;   // retries Open
await connection.OpenAsync();

await using var command = new SqlCommand("SELECT COUNT(*) FROM Users", connection);
command.RetryLogicProvider = retryProvider;      // optional: retries Execute*
var count = (int)await command.ExecuteScalarAsync();
```

Retry is **off by default**. Without these `RetryLogicProvider` assignments, nothing is retried.

## What the options mean

"Try up to 5 times, and wait longer between each retry."

| Option | Meaning |
|---|---|
| `NumberOfTries = 5` | 1 original attempt + 4 retries. After the 5th failure, an exception is thrown. |
| `DeltaTime = 1s` | Base delay; the provider grows the delay from it. |
| `MaxTimeInterval = 30s` | No single delay exceeds 30s. |

Rough timeline (real delays add random jitter):

```
Attempt 1 fail → wait ~1s
Attempt 2 fail → wait ~2s
Attempt 3 fail → wait ~4s
Attempt 4 fail → wait ~8s
Attempt 5 fail → throw
```

## What it helps with

Transient failures only: brief network interruptions, Azure SQL failover, throttling,
brief database unavailability. SqlClient has a built-in list of error numbers it retries
(see `error types/transient errors list.md`).

## Code review checks

1. Is retry enabled at all (`RetryLogicProvider` assigned)?
2. Is `NumberOfTries` reasonable (typically 3–5)?
3. Is it exponential backoff, not immediate retries?
4. Is the maximum delay bounded?
5. Is only transient failure retried?

Key question: *Is retry enabled, how many tries, and is it exponential backoff rather than retrying indefinitely?*
