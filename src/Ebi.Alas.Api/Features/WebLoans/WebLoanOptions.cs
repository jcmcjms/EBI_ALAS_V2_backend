using System.Text.RegularExpressions;

namespace Ebi.Alas.Api.Features.WebLoans;

public sealed partial class WebLoanOptions
{
    public const string SectionName = "WebLoan";

    public string? NamePrefix { get; set; }

    public static bool IsValidNamePrefix(string? namePrefix)
        => !string.IsNullOrWhiteSpace(namePrefix) && NamePrefixPattern().IsMatch(namePrefix);

    [GeneratedRegex("^[A-Za-z0-9_]+(\\.[A-Za-z0-9_]+)*$")]
    private static partial Regex NamePrefixPattern();
}
