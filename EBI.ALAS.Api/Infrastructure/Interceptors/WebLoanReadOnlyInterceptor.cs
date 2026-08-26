using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EBI.ALAS.Api.Infrastructure.Interceptors;

/// <summary>
/// Defense-in-depth guard for the WebLoan database: blocks any data-modifying
/// or schema-changing SQL command at the ADO.NET level, BEFORE it reaches
/// SQL Server. This catches raw SQL (FromSqlRaw / ExecuteSqlRaw / SqlQuery)
/// that would bypass the SaveChanges override on WebLoanDbContext.
///
/// Allowed: SELECT, and command types that only read (stored procs are NOT
/// allowed by default — enable case-by-case if the webloan system exposes
/// safe read procedures).
/// </summary>
public sealed class WebLoanReadOnlyInterceptor : DbCommandInterceptor
{
    // Commands that modify data or schema. Anything not starting with an
    // allowed read keyword AND matching one of these is rejected.
    private static readonly string[] ForbiddenPrefixes =
    [
        "INSERT", "UPDATE", "DELETE", "MERGE", "TRUNCATE",
        "CREATE", "ALTER", "DROP", "EXEC", "EXECUTE",
        "GRANT", "REVOKE", "DENY"
    ];

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        EnsureReadOnly(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        EnsureReadOnly(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    // Scalar/non-query executions should never happen for a read-only context;
    // block them outright rather than inspecting.
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        throw BlockedException(command);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        throw BlockedException(command);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        EnsureReadOnly(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        EnsureReadOnly(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private static void EnsureReadOnly(DbCommand command)
    {
        var text = command.CommandText.TrimStart();

        foreach (var prefix in ForbiddenPrefixes)
        {
            if (text.StartsWith(prefix + ' ', StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith(prefix + '(', StringComparison.OrdinalIgnoreCase) ||
                text.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith(prefix + '\r', StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith(prefix + '\n', StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith(prefix + ';', StringComparison.OrdinalIgnoreCase))
            {
                throw BlockedException(command);
            }
        }
    }

    private static InvalidOperationException BlockedException(DbCommand command) =>
        new(
            $"BLOCKED: The WebLoan database is READ-ONLY. " +
            $"Attempted command: '{Truncate(command.CommandText)}'. " +
            $"Only SELECT queries are permitted against webloan from this API.");

    private static string Truncate(string sql) =>
        sql.Length <= 120 ? sql : sql[..120] + "...";
}
