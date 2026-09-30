# Connection factory

Yes. A small ****SqlConnectionFactory**** is a good approach because it ensures every connection gets the same connection string and retry policy.  
The retry provider must be assigned **before** calling OpenAsync().  
## Simple factory class  
  
using Microsoft.Data.SqlClient;  
  
public sealed class SqlConnectionFactory  
{  
    private readonly string _connectionString;  
    private readonly SqlRetryLogicBaseProvider _connectionRetryProvider;  
  
    public SqlConnectionFactory(string connectionString)  
    {  
        _connectionString = connectionString;  
  
        var retryOptions = new SqlRetryLogicOption  
        {  
            NumberOfTries = 5, // One initial attempt + four retries  
            DeltaTime = TimeSpan.FromSeconds(2),  
            MaxTimeInterval = TimeSpan.FromSeconds(30)  
        };  
  
        _connectionRetryProvider =  
            SqlConfigurableRetryFactory  
                .CreateExponentialRetryProvider(retryOptions);  
    }  
  
    public SqlConnection CreateConnection()  
    {  
        return new SqlConnection(_connectionString)  
        {  
            RetryLogicProvider = _connectionRetryProvider  
        };  
    }  
}  
  
## Usage  
  
var factory = new SqlConnectionFactory(connectionString);  
  
await using var connection = factory.CreateConnection();  
await connection.OpenAsync();  
  
await using var command = new SqlCommand(  
    "SELECT Id, Name FROM Users",  
    connection);  
  
await using var reader = await command.ExecuteReaderAsync();  
  
while (await reader.ReadAsync())  
{  
    Console.WriteLine(reader["Name"]);  
}  
  
The caller should still dispose each returned connection. ADO.NET connection pooling is enabled by default, so disposing a connection normally returns the underlying physical connection to the pool instead of wasting it.  
## Important: This only configures connection retry  
This line:  
  
connection.RetryLogicProvider = _connectionRetryProvider;  
  
retries **opening the connection**. It does not automatically apply the provider to commands. Connection and command retry policies are configured separately.  
If you also want a command factory, keep it separate so command retry is an explicit decision:  
  
public sealed class SqlCommandFactory  
{  
    private readonly SqlRetryLogicBaseProvider _commandRetryProvider;  
  
    public SqlCommandFactory()  
    {  
        var retryOptions = new SqlRetryLogicOption  
        {  
            NumberOfTries = 5,  
            DeltaTime = TimeSpan.FromSeconds(2),  
            MaxTimeInterval = TimeSpan.FromSeconds(30),  
  
            // Only allow retry for commands considered safe to repeat.  
            AuthorizedSqlCondition = commandText =>  
                commandText.TrimStart()  
                           .StartsWith(  
                               "SELECT",  
                               StringComparison.OrdinalIgnoreCase)  
        };  
  
        _commandRetryProvider =  
            SqlConfigurableRetryFactory  
                .CreateExponentialRetryProvider(retryOptions);  
    }  
  
    public SqlCommand CreateCommand(  
        string sql,  
        SqlConnection connection)  
    {  
        return new SqlCommand(sql, connection)  
        {  
            RetryLogicProvider = _commandRetryProvider,  
            CommandTimeout = 30  
        };  
    }  
}  
  
Usage:  
  
await using var connection = connectionFactory.CreateConnection();  
await connection.OpenAsync();  
  
await using var command =  
    commandFactory.CreateCommand(  
        "SELECT Id, Name FROM Users",  
        connection);  
  
await using var reader = await command.ExecuteReaderAsync();  
  
## Recommendation  
* **Yes:** Centralize connection creation and connection retry.  
* **Do not:** Return one shared/static SqlConnection.  
* **Create and dispose:** A new SqlConnection object per database operation; let connection pooling handle physical connection reuse.  
* **Keep command retry explicit:** Automatically retrying writes can duplicate operations.  
* **For transactions:** Retry the entire transaction, not one command inside it. SqlClient’s built-in command provider does not retry commands on a connection with an active transaction.  
For most applications, I would implement the **connection factory first**, then add command retry only for operations that are clearly safe to repeat.
