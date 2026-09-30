# SqlClient Transient Errors

`SqlRetryLogicOption.TransientErrors` decides which SQL error numbers a retry provider retries.

**Most of the time, do not set it.** When it is `null`, SqlClient uses its built-in list below.
That is the right choice for most code.

## Built-in list (used when `TransientErrors` is `null`)

| Area | Errors |
|---|---|
| Connecting | 233, 997, 10060, 4060, 4221 |
| Locks and deadlocks | 1204, 1205, 1222 |
| Throttling and limits | 10928, 10929, 40501, 49918, 49919, 49920 |
| Azure SQL failover | 40143, 40197, 40540, 40613 |
| Synapse pool paused or resuming | 42108, 42109 |

## When to set `TransientErrors`

Only when you need a different set:

- **Add** an error the built-in list lacks, e.g. 3960 (snapshot isolation update conflict) if the app uses snapshot isolation.
- **Remove** an error on purpose.
- **Demo**, e.g. 208 or 2812 to trigger retries on demand.

## Critical: setting it replaces the built-in list

`TransientErrors` does **not** add to the built-in list. It replaces it.
A short hand-picked list silently drops every built-in error you left out.

```csharp
// Only these 4 are retried. 10928, 10929, 40143, 49918... are no longer retried.
TransientErrors = new[] { 1205, 40197, 40501, 40613 }
```

## How to add an error safely

- **SqlClient 7.0+**: start from the built-in list.

  ```csharp
  TransientErrors = SqlConfigurableRetryFactory.BaselineTransientErrors.Append(3960).ToArray()
  ```

- **SqlClient 5.x** (no `BaselineTransientErrors`): copy the full list from
  `SqlConfigurableRetryFactory.cs` at the [SqlClient source tag](https://github.com/dotnet/SqlClient/tags)
  matching your package version, then add your error. The list on `main` can differ.

## Code review checks

| What you see | Verdict |
|---|---|
| `TransientErrors` not set | OK: the built-in list is used |
| `TransientErrors` set | Check it still includes every built-in error the app needs |
| List contains 102 (syntax), 207 (invalid column), 208 (invalid object), 2812 (missing procedure) | Wrong: these are not transient; retrying only delays the same failure |

## Source

- [Built-In Retry Logic Providers in SqlClient](https://learn.microsoft.com/en-us/sql/connect/ado-net/internal-retry-logic-providers-sqlclient)
