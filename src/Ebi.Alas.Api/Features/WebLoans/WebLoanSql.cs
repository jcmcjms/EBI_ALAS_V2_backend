namespace Ebi.Alas.Api.Features.WebLoans;

public static class WebLoanSql
{
    public static string Qualify(string? namePrefix, string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return string.IsNullOrWhiteSpace(namePrefix)
            ? tableName
            : $"{namePrefix}.{tableName}";
    }
}
