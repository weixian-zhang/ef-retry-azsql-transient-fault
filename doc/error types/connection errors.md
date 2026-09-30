# Transient connection errors

Errors while opening the connection (`Open` / `OpenAsync`).
The **connection** retry provider (`connection.RetryLogicProvider`) retries these.

Examples:

* Azure SQL failover during login
* Temporary network outage
* Database temporarily unavailable
* Authentication connection failures
* Connection establishment timeout

| Error | Example |
|---|---|
| 40613: database not currently available | Azure moves the database to another server during maintenance. Logins fail for a few seconds, then succeed. |
| 4060: cannot open database | A serverless Azure SQL database has auto-paused. The first login fails while it resumes. |
| 10060: connection attempt timed out | A brief network problem means the first attempt gets no answer. The retry goes through. |

Note: "Authentication connection failures" means the connection dropping during login. A wrong
password (18456, login failed) is not transient; retrying it only delays the same failure.
