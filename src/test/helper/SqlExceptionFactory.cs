using System;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace RetryTests;

/// <summary>
/// SqlException has no public constructor, so tests build one through SqlClient's internal
/// members (the same approach EF Core's own tests use). Written against Microsoft.Data.SqlClient 5.2.
/// </summary>
internal static class SqlExceptionFactory
{
    private const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static SqlException Create(int errorNumber, string errorMessage)
    {
        // internal SqlError(int infoNumber, byte errorState, byte errorClass, string server,
        //                   string errorMessage, string procedure, int lineNumber, Exception exception)
        var errorConstructor = typeof(SqlError).GetConstructor(Internal, binder: null,
                new[] { typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception) },
                modifiers: null)
            ?? throw new InvalidOperationException("SqlError constructor not found; SqlClient internals changed.");
        var error = errorConstructor.Invoke(new object?[] { errorNumber, (byte)0, (byte)0, "test", errorMessage, "test", 0, null });

        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var addError = typeof(SqlErrorCollection).GetMethod("Add", Internal)
            ?? throw new InvalidOperationException("SqlErrorCollection.Add not found; SqlClient internals changed.");
        addError.Invoke(errors, new[] { error });

        var createException = typeof(SqlException).GetMethod("CreateException", Internal, binder: null,
                new[] { typeof(SqlErrorCollection), typeof(string) }, modifiers: null)
            ?? throw new InvalidOperationException("SqlException.CreateException not found; SqlClient internals changed.");
            
        return (SqlException)createException.Invoke(null, new object[] { errors, "16.0" })!;
    }
}
