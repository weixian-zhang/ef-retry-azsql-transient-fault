# Unit and integration test plan: EF Core retry on Azure SQL transient faults

## Goal

Prove, against .NET 6 + EF Core 6 + Microsoft.Data.SqlClient 5.2.2 + Azure SQL with managed identity, that:
1. transient failures are retried,
2. non-transient failures are not,
3. a transaction is retried as a whole unit, and commits only once.

Two suites:

| Suite | Location | Talks to | Speed |
|---|---|---|---|
| Unit tests | `src/test` (exists, 8 tests passing) | Nothing: faults injected by an EF connection interceptor | < 1s |
| Integration tests | `src/integration-test` (to build) | Real Azure SQL; network faults via firewall | Minutes, Windows only |

---

## Critical points to know before writing tests

### Who retries what

| Mechanism | Retries | Does not retry |
|---|---|---|
| SqlClient `connection.RetryLogicProvider` | `Open` / `OpenAsync` only | Commands. It never reconnects a connection that dropped later. |
| SqlClient `command.RetryLogicProvider` | `Execute*` calls | Commands inside a `SqlTransaction` or `TransactionScope`; commands rejected by `AuthorizedSqlCondition`; errors while reading rows (`Read`/`ReadAsync`) |
| EF `EnableRetryOnFailure` | Queries, `SaveChanges`, and the whole transaction when wrapped in `CreateExecutionStrategy().ExecuteAsync(...)` | Errors not on EF's list |

- **Retry is off by default** in SqlClient; a `RetryLogicProvider` must be assigned. On SqlClient 3.x it also needs `AppContext` switch `Switch.Microsoft.Data.SqlClient.EnableRetryLogic`; from 4.0 on it doesn't.
- **With EF retry on, `BeginTransaction` outside `ExecuteAsync` throws `InvalidOperationException`.** Every user transaction must be wrapped.
- **Don't stack retries.** EF strategy + SqlClient provider, or a retry loop/Polly + provider, multiply the attempts (e.g. 5 × 5).
- **The work inside a retry must be safe to repeat.** If the connection drops during `Commit`, you can't know whether it committed. Use a unique business key so a repeat can't create a duplicate.

### Counting and exceptions

| | SqlClient provider | EF strategy | Polly |
|---|---|---|---|
| Count setting | `NumberOfTries` = total attempts (5 = 1 + 4 retries) | `maxRetryCount` = retries only (5 = 1 + 5 retries) | `MaxRetryAttempts` = retries only |
| After the last retry | `AggregateException` (one inner exception per attempt) | `RetryLimitExceededException` (last error in `InnerException`) | The original exception, rethrown |
| Not retryable | `SqlException` straight away | The original exception (`SqlException` / `DbUpdateException`) | The original exception |

A `catch (SqlException)` on its own **misses** SqlClient's "retried and still failed" case.

### Which errors are transient

- **SqlClient `TransientErrors` replaces the built-in list; EF `errorNumbersToAdd` adds to its list.**
- **SqlClient 5.2.2 built-in list (20 errors):** 1204, 1205, 1222, 10928, 10929, 40501, 49918, 49919, 49920, 4060, 4221, 40143, 40197, 40540, 40613, 233, 997, 10060, 42108, 42109. Checked against the source at tag `v5.2.2`.
- **EF Core 6.0.36 list** is larger. For transport errors it covers 233, 997, 10060, 64, 121, 10053, 10054, and it treats any `TimeoutException` as transient. Checked against `SqlServerTransientExceptionDetector.cs` at tag `v6.0.36`.
- **Not transient; never retry:** 102 (syntax), 207 (invalid column), 208 (invalid object), 2812 (missing procedure), 18456 (login failed). 208 and 2812 appear in the demo code **only** to trigger retries on demand.
- **Error numbers depend on the OS.** 10060/10053/10054 are Windows socket codes. On Linux/macOS the same failure may come back with a different number, e.g. 0 ("error: 0 - Success"). If the number isn't on the list, it isn't retried. **Test on the same OS as production, and log `SqlError.Number` for every failure.**

### Where EF opens the connection

EF opens connections lazily. Constructing the `DbContext` or building a LINQ query does **not** connect. These do:
- `ToListAsync()`, `FirstAsync()`, `CountAsync()`, `AnyAsync()`
- `SaveChangesAsync()`
- `Database.BeginTransactionAsync()`
- `Database.OpenConnectionAsync()`
- `Database.ExecuteSqlRawAsync()`

To catch the exact moment, use a `DbConnectionInterceptor.ConnectionOpeningAsync`, which runs just before **every** physical open, including each retry.

### Watching retries

- **SqlClient:** `provider.Retrying` event (`RetryCount`, `Delay`, `Exceptions`, `Cancel`). It doesn't fire on the first attempt, on success, or on the final failure.
- **EF:** there's no C# event. The options are:
  - log event `CoreEventId.ExecutionStrategyRetrying`, or
  - subclass `SqlServerRetryingExecutionStrategy` and override `OnRetry()` (watcher) and `GetNextDelay()` (custom base delay), or
  - a connection or command interceptor.
- **EF backoff is always exponential with small jitter.** The base delay is about 1s and can't be set through `EnableRetryOnFailure`. The exact delays haven't been checked; the log shows the real values.

### Environment traps

- **Connection pooling can hide failures.** An "open" may reuse a pooled live connection. In tests, use `Pooling=false` or call `SqlConnection.ClearAllPools()` before each test.
- **Managed identity:** transport errors happen before the token is used, so they're independent of authentication. Token failures are a different kind of error, and neither EF nor SqlClient retries them. Locally, use `Authentication=Active Directory Default`; on an Azure host, `Active Directory Managed Identity`.
- **Azure SQL Redirect policy** (the default inside Azure) connects to ports **11000–11999** after login, not just 1433.
- **Timeouts apply per attempt.** The worst case is roughly attempts × timeout + all delays.

---

## Custom execution strategy: extend `SqlServerRetryingExecutionStrategy`

Both suites use a custom strategy instead of plain `EnableRetryOnFailure`, because the tests need things `EnableRetryOnFailure` can't give:

| Need | `EnableRetryOnFailure` | Custom strategy |
|---|---|---|
| A retry watcher in code: count, error number, delay, timestamp | Log text only | Override `OnRetry()` |
| A hook to act on retry #N, e.g. restore the firewall | No | Call it from `OnRetry()` |
| Your own base delay (e.g. 2s to match the production policy), or near-zero delay for unit tests | No; base is fixed at ~1s | Override `GetNextDelay()` |
| Keep EF's SQL Server list of transient errors | Yes | Yes; the base class keeps it |

### Shape

```csharp
internal sealed class RecordingRetryStrategy : SqlServerRetryingExecutionStrategy
{
    public RecordingRetryStrategy(ExecutionStrategyDependencies dependencies, RetryLog retryLog,
        int maxRetryCount, TimeSpan baseDelay, TimeSpan maxDelay)
        : base(dependencies, maxRetryCount, maxDelay, errorNumbersToAdd: null) { ... }

    // Keep EF's decision (null = stop), replace only the delay: baseDelay x 2^(n-1), capped at maxDelay.
    protected override TimeSpan? GetNextDelay(Exception lastException) { ... }

    // The watcher: record retry number, SqlError.Number, delay and timestamp; run the optional hook.
    protected override void OnRetry() { ... }
}

// in the test DbContext, instead of EnableRetryOnFailure (use one or the other):
sqlServer.ExecutionStrategy(dependencies => new RecordingRetryStrategy(dependencies, retryLog, ...));
```

### Rules

- **Pass values in through the constructor.** The retry count, delays, `RetryLog` and the optional `onRetry` hook all come in this way; don't use static values. EF calls the factory for each execution, so the test owns the `RetryLog` and passes it through the `DbContext`.
- **Unwrap the error before recording `SqlError.Number`.** In `ExceptionsEncountered`, a `SaveChanges` failure is a `DbUpdateException` with the `SqlException` inside it.
- **Don't override `ShouldRetryOn()`** unless a test needs a different error list. Overriding it means the test no longer checks EF's real decision.
- **One copy per test project.** Unit and integration tests stay self-contained, so each has its own copy and neither references the other or `src/`.
- **Base it on the deleted `EntityFramework_CustomRetryStrategyScenario.cs`.** Its `WatchedSqlServerRetryingExecutionStrategy` compiled against EF Core 6, which confirms `OnRetry()`, `GetNextDelay()` and `ExceptionsEncountered` exist. Take the file from git history or the earlier conversation.

---

## Suite 1: Unit tests (`src/test`, done)

A self-contained xUnit project (net6.0, EF Core 6.0.36, SqlClient 5.2.2). It has no reference to the main project.

| File | Role |
|---|---|
| `OrderDbContext.cs` | Test context with `EnableRetryOnFailure(3 retries, 1 ms delay)`; takes the interceptor through its constructor |
| `TransientNetworkFailureTests.cs` | The tests, plus `FailingConnectionInterceptor` and `SqlExceptionFactory` |
| `RecordingRetryStrategy.cs` (to add) | Custom strategy extending `SqlServerRetryingExecutionStrategy`, plus `RetryLog` |

- **How faults are injected:** `ConnectionOpeningAsync` throws a `SqlException` with the chosen number for the first N attempts, then returns `InterceptionResult.Suppress()`. EF then treats the connection as open without any network.
- **`SqlExceptionFactory`:** `SqlException` has no public constructor, so it's built through SqlClient internals via reflection, as EF Core's own tests do. It was written for SqlClient 5.2, and it throws a clear error if the internals change.
- **Covered:**
  - transport errors 233, 997, 10060, 64, 10053, 10054 are retried until the open succeeds;
  - a network that stays down gives up after max retries with `RetryLimitExceededException`;
  - 18456 is not retried.
- **Run:** `cd src/test && dotnet test`
- **To add next:**
  - **Switch `OrderDbContext` to the custom strategy** (`RecordingRetryStrategy`, 1 ms base delay), passing in a `RetryLog`.
  - **Test the strategy itself:**
    - `OnRetry` fires once per retry, not on the first attempt or the final failure;
    - the recorded `SqlError.Number` matches the injected error;
    - the delays follow `base x 2^(n-1)` and are capped at the max;
    - `GetNextDelay()` runs before `OnRetry()`, so the recorded delay belongs to the current retry. This is unconfirmed; this test settles it.
  - failover and availability errors (40613, 40197, 4060) on open;
  - a `DbCommandInterceptor` that fails `SaveChanges` mid-transaction, to prove the whole transaction runs again (`RetryLog` shows the `SqlException` unwrapped from `DbUpdateException`).

These tests prove **EF's decision for each error number**. They don't prove what number a real outage produces; that's what suite 2 is for.

---

## Suite 2: Integration tests against real Azure SQL (to build)

### Don't flip the firewall profile's default outbound action

`Set-NetFirewallProfile -DefaultOutboundAction Block` is too broad:
- **It blocks all outbound traffic:** DNS, the managed identity token endpoint (`169.254.169.254`), Entra ID sign-in, and the CI agent's connection to its server. The job can hang, and token failures show up instead of SQL transient errors.
- **It likely fails with an error unlike a real outage.** A local block probably fails immediately with 10013, which isn't on EF's list. A real outage is usually a timeout (10060), a reset (10054) or a failover (40613/40197/4060). This hasn't been confirmed; scenario 1 will show it.
- **It leaves open connections alone,** because the firewall is stateful, so pooled connections keep working.
- **It changes the whole machine,** and stays that way if a test crashes.

### Use a named rule that only blocks Azure SQL traffic

```powershell
# block
New-NetFirewallRule -DisplayName "RetryTest-BlockAzureSql" -Direction Outbound `
    -Protocol TCP -RemotePort 1433,11000-11999 -Action Block
# restore (safe to run repeatedly)
Remove-NetFirewallRule -DisplayName "RetryTest-BlockAzureSql" -ErrorAction SilentlyContinue
```

### Design

- **Project:** `src/integration-test/RetryIntegrationTests.csproj` (net6.0, xUnit), self-contained, with nothing faked.
- **Where it runs:** a dedicated Windows VM in Azure with the managed identity assigned and the same OS as production. Tag the tests `[Trait("Category", "NetworkFault")]` and keep them out of the normal CI run.
- **`FirewallFault` helper:**
  - runs `powershell.exe -NoProfile -NonInteractive -Command ...` through `Process.Start`;
  - checks for admin rights first and throws if it doesn't have them;
  - checks the exit code and stderr, and throws with the output on failure.
- **Always restore the firewall:** in `IAsyncLifetime.DisposeAsync`, and also in a CI cleanup step that always runs.
- **No parallel runs:** `[Collection("Firewall")]` with `DisableParallelization = true`.
- **Connection string:** `Authentication=Active Directory Managed Identity; Pooling=false; Connect Timeout=5`.
- **Watcher:** `RecordingRetryStrategy`, with production-like settings (e.g. 5 retries, 2s base, 30s cap), records `SqlError.Number`, delay and timestamp for every retry. Its `onRetry` hook is how a test restores the firewall at retry #N, without relying on timing.

### Scenarios

| # | Scenario | How | Assert |
|---|---|---|---|
| 1 | Network down, never recovers | Block, then run a query | `RetryLimitExceededException`; attempts = max + 1; **log which error number the firewall produces** |
| 2 | Network down, then recovers | Block; `RecordingRetryStrategy`'s `onRetry` hook calls `FirewallFault.RestoreAsync()` on retry #2 | Succeeds; `RetryLog` shows exactly 2 retries |
| 3 | Connection lost mid-transaction | Start the transaction; an admin connection runs `KILL <session_id>` | The whole transaction is retried and commits **once** (unique key, row count = 1) |
| 4 | Real Azure failover (optional) | `az sql db failover` while a query loop runs | Recovers; 40613/40197/4060 recorded; no data loss |

If scenario 1 reports 10013, note that the firewall doesn't imitate a real outage, and rely on scenarios 3 and 4 for realism. A cross-platform alternative is a Toxiproxy `timeout` fault in front of the database. It needs `HostNameInCertificate=*.database.windows.net` and only works with the Proxy connection policy.

### Files

- `src/integration-test/RetryIntegrationTests.csproj`
- `src/integration-test/FirewallFault.cs`
- `src/integration-test/RecordingRetryStrategy.cs` (its own copy of the custom strategy and `RetryLog`)
- `src/integration-test/NetworkFaultTests.cs`
- `src/integration-test/README.md`: run elevated on the VM, managed identity setup, the manual cleanup command

### Verification

- Elevated on the VM: `dotnet test src/integration-test --filter Category=NetworkFault`
- After every run, `Get-NetFirewallRule -DisplayName "RetryTest-BlockAzureSql"` returns nothing.
- The logged error numbers match expectations for each scenario.

---

## Open items

- **Package mix:** EF Core 6.0.36 is used with SqlClient 5.2.2 (EF 6 was built against 2.1). It compiles, but hasn't been tested at runtime.
- **Unconfirmed:**
  - the firewall block's error number (expected 10013);
  - EF's exact default delays (the custom strategy makes these deterministic);
  - the order in which EF calls `GetNextDelay()` and `OnRetry()` (a unit test will settle it);
  - whether SqlClient applies connection retry inside a `TransactionScope`;
  - whether command retry can recover a dropped connection (expected: no).
- **Nothing has been run against a real database yet.**
