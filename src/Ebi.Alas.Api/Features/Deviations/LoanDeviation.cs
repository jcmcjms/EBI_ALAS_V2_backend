namespace Ebi.Alas.Api.Features.Deviations;

public sealed class LoanDeviation
{
    private LoanDeviation()
    {
        Code = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid LoanApplicationId { get; private set; }

    public string Code { get; private set; }

    public string Description { get; private set; }

    public int Severity { get; private set; }

    public static LoanDeviation Create(
        Guid loanApplicationId,
        string code,
        string description,
        int severity,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegative(severity);
        return new LoanDeviation
        {
            Id = Guid.NewGuid(),
            LoanApplicationId = loanApplicationId,
            Code = code.Trim(),
            Description = description.Trim(),
            Severity = severity
        };
    }
}
