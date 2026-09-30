# Connection vs command timeout

## Connection Timeout vs Command Timeout  
These are two different things.  

| Setting | What it controls | When it starts | Default |
| ------------------ | --------------------------------------------------- | --------------------------------------------------- | ---------- |
| Connection Timeout | How long to wait to establish a database connection | Open() / OpenAsync() | 15 seconds |
| Command Timeout | How long a SQL query/command can run | ExecuteReader(), ExecuteScalar(), ExecuteNonQuery() | 30 seconds |
  
## Connection Timeout  
Controls the time allowed to:  
* DNS resolution  
* Network connection  
* Authentication  
* Obtaining a connection from the pool  
Example:  
  
Server=myserver.database.windows.net;  
Database=MyDb;  
Connect Timeout=30;  
  
  
await connection.OpenAsync();  
  
If the database cannot be reached within 30 seconds, OpenAsync() throws an exception.  
Think of it as:  
"How long am I willing to wait to connect to SQL?"  
  
## Command Timeout  
Controls the time allowed for SQL execution after the connection is already open.  
Example:  
  
command.CommandTimeout = 60;  
  
  
await command.ExecuteReaderAsync();  
  
If the query runs longer than 60 seconds, SQL Client cancels the command and throws a timeout exception.  
Think of it as:  
"How long am I willing to wait for this query to finish?"  
  
## Example Timeline  
  
Application starts  
  
Open SQL Connection  
├─ Wait 10 seconds  
└─ Connected ✅  
  
Execute Query  
├─ Query runs 45 seconds  
└─ Query completed ✅  
  
If:  
  
Connect Timeout = 15  
Command Timeout = 30  
  
Then:  
* Connection succeeds because 10 < 15 seconds  
* Query fails because 45 > 30 seconds  
  
## Code Review Questions  
**Connection Timeout**  
Ask:  
* Is Connect Timeout explicitly configured?  
* Is it reasonable (typically 15–30 seconds)?  
* Is the application hiding slow networking issues by setting it to very large values?  
Red flag:  
  
Connect Timeout=300  
  
(Waiting 5 minutes for a connection usually indicates a problem.)  
  
**Command Timeout**  
Ask:  
* Is CommandTimeout configured appropriately?  
* Are long-running queries optimized instead?  
* Is timeout set to unlimited?  
Red flag:  
  
command.CommandTimeout = 0;  
  
A value of 0 means no timeout limit and can allow queries to hang indefinitely.  
  
## Recommended Azure SQL Defaults  
For most enterprise APIs:  
  
Connect Timeout = 30  
Command Timeout = 30  
  
If running reports or analytics:  
  
Connect Timeout = 30  
Command Timeout = 120  
  
## Simple Memory Aid  
* **Connection Timeout** = "Can I reach SQL?"  
* **Command Timeout** = "Can SQL finish the query?"
