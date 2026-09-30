using System;
using System.Linq;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace RetryTests;

/// <summary>
/// SqlException has no public constructor, so tests build one through SqlClient's internal
/// members (the same approach EF Core's own tests use). Written against Microsoft.Data.SqlClient 5.2.
/// </summary>
internal static class SqlExceptionFactory
{
    public static SqlException Create(int errorNumber, string errorMessage)
    {
        var errorCollection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var addError = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SqlErrorCollection.Add not found; SqlClient internals changed.");
        addError.Invoke(errorCollection, new object[] { CreateSqlError(errorNumber, errorMessage) });

        var createException = typeof(SqlException).GetMethod(
                "CreateException",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(SqlErrorCollection), typeof(string) },
                modifiers: null)
            ?? throw new InvalidOperationException("SqlException.CreateException not found; SqlClient internals changed.");
        return (SqlException)createException.Invoke(null, new object[] { errorCollection, "16.0" })!;
    }

    private static SqlError CreateSqlError(int errorNumber, string errorMessage)
    {
        var constructor = typeof(SqlError)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .First();

        var arguments = constructor.GetParameters()
            .Select(parameter => ArgumentFor(parameter, errorNumber, errorMessage))
            .ToArray();

        return (SqlError)constructor.Invoke(arguments);
    }

    private static object? ArgumentFor(ParameterInfo parameter, int errorNumber, string errorMessage) => parameter.Name switch
    {
        "infoNumber" => errorNumber,
        "errorMessage" => errorMessage,
        _ when parameter.ParameterType == typeof(string) => "test",
        _ when parameter.ParameterType == typeof(byte) => (byte)0,
        _ when parameter.ParameterType == typeof(int) => 0,
        _ when parameter.ParameterType == typeof(uint) => 0u,
        _ when !parameter.ParameterType.IsValueType => null,
        _ => throw new InvalidOperationException($"Unexpected SqlError constructor parameter '{parameter.Name}' of type {parameter.ParameterType}."),
    };
}
