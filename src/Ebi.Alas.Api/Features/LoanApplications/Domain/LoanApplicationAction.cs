namespace Ebi.Alas.Api.Features.LoanApplications.Domain;

public sealed class LoanApplicationAction
{
    private LoanApplicationAction()
    {
        Action = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid LoanApplicationId { get; private set; }

    public Guid ActionByUserId { get; private set; }

    public string Action { get; private set; }

    public string? FromStatus { get; private set; }

    public string? ToStatus { get; private set; }

    public string? Comments { get; private set; }

    public DateTimeOffset ActionDate { get; private set; }

    public static LoanApplicationAction Create(
        Guid loanApplicationId,
        Guid actionByUserId,
        string action,
        string? fromStatus,
        string? toStatus,
        string? comments,
        DateTimeOffset actionDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return new LoanApplicationAction
        {
            Id = Guid.NewGuid(),
            LoanApplicationId = loanApplicationId,
            ActionByUserId = actionByUserId,
            Action = action.Trim(),
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Comments = comments,
            ActionDate = actionDate,
        };
    }
}
