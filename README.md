# ef-retry-transient-fault

## Local_No_DB_Transient_Error_Tests

`src/test/Local_No_DB_Transient_Error_Tests.cs` checks how Entity Framework's retry strategy reacts to SQL transient errors, such as those seen during an Azure SQL failover.

It needs **no Azure SQL database and no network blockage**. An Entity Framework connection interceptor catches each attempt to open the SQL connection and throws the chosen transient error instead. EF then retries or gives up exactly as it would against a real database.

## Block_Outbound_Internet_Transient_Error_Tests

`src/test/Block_Outbound_Internet_Transient_Error_Tests.cs` causes a **real network failure** against a real Azure SQL database, without needing admin access to Azure.

It runs a SELECT, then adds a Windows Firewall rule on the test machine that blocks outbound SQL ports (1433, 11000-11999) and runs the SELECT again. The test passes when Entity Framework retries until it runs out of retries, which shows the retry strategy treated the failure as transient. The rule is always removed afterwards.

Run it on a Windows VM, from an elevated shell.

### ConnectionString

Required. In the test file, replace `<server>` and `<database>` in the fake `ConnectionString` with the real Azure SQL database. It signs in with the VM's managed identity.
