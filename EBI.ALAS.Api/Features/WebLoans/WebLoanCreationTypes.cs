namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Labels for loan_data.creation_type codes in the WebLoan database.
/// Source: webloan domain knowledge (no lookup table exists for this column).
/// </summary>
public static class WebLoanCreationTypes
{
    public const byte NewLoan = 0;
    public const byte Reloan = 1;
    public const byte Reconstructed = 2;
    public const byte Continuation = 3;
    public const byte Extension = 4;
    public const byte Renewal = 5;
    public const byte AdditionalLoan = 6;

    private static readonly Dictionary<byte, string> Labels = new()
    {
        [NewLoan] = "New Loan",
        [Reloan] = "Reloan",
        [Reconstructed] = "Reconstructed",
        [Continuation] = "Continuation",
        [Extension] = "Extension",
        [Renewal] = "Renewal",
        [AdditionalLoan] = "Additional Loan"
    };

    /// <summary>Returns the display label for a creation_type code, or null if unknown.</summary>
    public static string? GetLabel(byte? code) =>
        code is null ? null : Labels.TryGetValue(code.Value, out var label) ? label : null;
}
