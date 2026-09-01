namespace EBI.ALAS.Api.Features.WebLoans;
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

    public static string? GetLabel(byte? code) =>
        code is null ? null : Labels.TryGetValue(code.Value, out var label) ? label : null;
}
