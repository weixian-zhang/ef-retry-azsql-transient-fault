# ef-retry-transient-fault

## Local_No_DB_Transient_Error_Tests

`src/test/Local_No_DB_Transient_Error_Tests.cs` checks how Entity Framework's retry strategy reacts to SQL transient errors, such as those seen during an Azure SQL failover.

It needs **no Azure SQL database and no network blockage**. An Entity Framework connection interceptor catches each attempt to open the SQL connection and throws the chosen transient error instead. EF then retries or gives up exactly as it would against a real database.
