using System.Text.RegularExpressions;

namespace Ebi.Alas.Api.Features.WebLoans;

public sealed partial class WebLoanOptions
{
    public const string SectionName = "WebLoan";

    /// <summary>WebLoan database name on the ALAS SQL Server host (e.g. webloan).</summary>
    public string? Database { get; set; }

    public static bool IsValidDatabaseName(string? database)
        => !string.IsNullOrWhiteSpace(database) && DatabaseNamePattern().IsMatch(database);

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex DatabaseNamePattern();
}
