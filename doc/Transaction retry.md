# Transaction retry

I assume you mean a simple ****SqlCommand transaction retry pattern**** using Microsoft.Data.SqlClient.  
Microsoft recommends **not assigning command retry providers to individual commands inside a transaction**. Instead, wrap the **connection, transaction, commands, and commit** in one retry loop. If a transient failure occurs, create a new connection and transaction, then replay the entire operation.  
  
using Microsoft.Data.SqlClient;  
  
public static async Task CreateOrderAsync(  
    string connectionString,  
    int orderId,  
    CancellationToken cancellationToken = default)  
{  
    const int maxAttempts = 5;  
  
    for (int attempt = 1; attempt <= maxAttempts; attempt++)  
    {  
        try  
        {  
            // Create a new connection for every attempt  
            await using var connection =  
                new SqlConnection(connectionString);  
  
            await connection.OpenAsync(cancellationToken);  
  
            await using var transaction =  
                await connection.BeginTransactionAsync(cancellationToken);  
  
            try  
            {  
                // Command 1  
                await using var orderCommand = new SqlCommand(  
                    """  
                    INSERT INTO Orders (Id, Status)  
                    VALUES (@Id, @Status)  
                    """,  
                    connection,  
                    (SqlTransaction)transaction);  
  
                orderCommand.Parameters.AddWithValue("@Id", orderId);  
                orderCommand.Parameters.AddWithValue("@Status", "Created");  
  
                await orderCommand.ExecuteNonQueryAsync(cancellationToken);  
  
                // Command 2  
                await using var auditCommand = new SqlCommand(  
                    """  
                    INSERT INTO AuditLog (OrderId, Message)  
                    VALUES (@OrderId, @Message)  
                    """,  
                    connection,  
                    (SqlTransaction)transaction);  
  
                auditCommand.Parameters.AddWithValue("@OrderId", orderId);  
                auditCommand.Parameters.AddWithValue(  
                    "@Message",  
                    "Order created");  
  
                await auditCommand.ExecuteNonQueryAsync(cancellationToken);  
  
                // Both commands succeed or fail together  
                await transaction.CommitAsync(cancellationToken);  
                return;  
            }  
            catch  
            {  
                await transaction.RollbackAsync(CancellationToken.None);  
                throw;  
            }  
        }  
        catch (SqlException ex) when (  
            IsTransient(ex) && attempt < maxAttempts)  
        {  
            // Simple exponential backoff: 2, 4, 8, 16 seconds  
            var delay = TimeSpan.FromSeconds(  
                Math.Pow(2, attempt));  
  
            Console.WriteLine(  
                $"Transaction failed. Retrying attempt " +  
                $"{attempt + 1}/{maxAttempts} in {delay.TotalSeconds}s.");  
  
            await Task.Delay(delay, cancellationToken);  
        }  
    }  
}  
  
A simple transient-error check:  
  
private static bool IsTransient(SqlException exception)  
{  
    int[] transientErrors =  
    [  
        1205,  // Deadlock  
        4060,  // Database unavailable  
        10928, // Resource limit  
        10929, // Resource limit  
        40143, // Azure SQL service error  
        40197, // Azure SQL service error/failover  
        40501, // Service busy  
        40613, // Database unavailable  
        49918,  
        49919,  
        49920  
    ];  
  
    return exception.Errors  
        .Cast<SqlError>()  
        .Any(error => transientErrors.Contains(error.Number));  
}  
  
## The important pattern  
  
Attempt 1  
├── Create new connection  
├── Begin new transaction  
├── Execute command 1  
├── Execute command 2  
└── Commit  
       ↓ transient failure  
  
Attempt 2  
├── Create new connection  
├── Begin new transaction  
├── Execute command 1 again  
├── Execute command 2 again  
└── Commit  
  
Do **not** set this inside the transaction:  
  
command.RetryLogicProvider = retryProvider;  
  
SqlClient’s built-in command retry provider does not retry commands on a connection with an active transaction. If the transaction becomes invalid, retrying only one statement could produce incorrect or partial results.  
## Critical production consideration  
Use an idempotency or unique business key, such as the orderId primary key in this example. If the connection fails while CommitAsync() is completing, the application may not know whether the transaction committed. Retrying without duplicate protection could create duplicate records.  
For production code, also:  
* Add random jitter to the retry delay.  
* Log each retry.  
* Keep retries bounded.  
* Use parameterized queries.  
* Pass CancellationToken.  
* Recreate the connection and transaction for every attempt.  
* Do not retry non-transient errors. Microsoft’s built-in exponential providers add jitter and recognize a maintained list of transient errors, but their command provider is intentionally not used within an active transaction.
