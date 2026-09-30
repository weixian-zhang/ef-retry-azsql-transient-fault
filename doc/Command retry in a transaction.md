# Command retry in a transaction

Yes—**conceptually, you are right**:  
  
INSERT Order       ✅ succeeds  
INSERT AuditLog    ❌ transient failure  
INSERT AuditLog    🔄 command-level retry  
  
A command-level retry retries only the **AuditLog command**, not the earlier Order command.  
However, inside an active SQL transaction, Microsoft.Data.SqlClient’s built-in command retry provider intentionally **does not retry the failed command**. Microsoft recommends rolling back and retrying the entire transaction instead.  
Therefore, the actual recommended flow is:  
  
Attempt 1  
├── Begin transaction  
├── INSERT Order       ✅  
├── INSERT AuditLog    ❌  
└── Roll back everything  
    ├── Order insert rolled back  
    └── Audit insert rolled back  
  
Attempt 2  
├── Begin new transaction  
├── INSERT Order       🔄 run again  
├── INSERT AuditLog    🔄 run again  
└── Commit             ✅  
  
Because both inserts are in the same transaction, rolling back removes the successful Order insert too. The next attempt then executes both commands again as one unit.  
## Simple distinction  
* **If there is no transaction:** command retry retries only INSERT AuditLog.  
* **If there is an active transaction:** built-in command retry does not retry the individual command; the application should roll back and retry both INSERT Order and INSERT AuditLog.  
So your understanding is right about how **individual command retry generally works**, but SqlClient avoids that behavior inside a transaction because retrying only part of the transaction may be unsafe.
