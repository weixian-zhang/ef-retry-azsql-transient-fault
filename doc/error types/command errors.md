# Transient command errors

Errors while a command runs on an open connection (`ExecuteReader`, `ExecuteScalar`, `ExecuteNonQuery`).
The **command** retry provider (`command.RetryLogicProvider`) retries these, but only when the
error is in `TransientErrors`, `AuthorizedSqlCondition` allows the command, and the command is
not inside a transaction.

Examples:

* Azure SQL transient error during query execution
* Many requests arrive at once and Azure SQL briefly throttles your database.
* SQL failover during execution
* Transport interruption after connection established

| Error | Example |
|---|---|
| 1205: deadlock victim | A `SELECT` and another user's `UPDATE` block each other. SQL Server cancels the `SELECT`; running it again works. |
| 40501: service busy | Many requests arrive at once and Azure SQL throttles the database. Wait about 10 seconds, then retry. |
| 10928: resource limit reached | The database hits its worker or session limit during a traffic spike. The retry succeeds once other queries finish. |

Note: failover or transport errors during execution (e.g. 40197, 10054) usually break the
connection itself. The command provider re-runs the command on the same connection object, so it
may not recover. Retry the whole operation with a new connection instead.
