namespace Ebi.Alas.Api.Features.LoanApplications.Domain;

/// <summary>
/// Unsubmitted review remarks for one reviewer on one application. The review
/// desk auto-saves this as the officer types so progress survives refreshes,
/// navigation and device changes. Cleared when the loan changes stage.
/// </summary>
public sealed class ReviewDraft
{
    public const int MaxRemarksLength = 2000;

    private ReviewDraft()
    {
        Remarks = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid LoanApplicationId { get; private set; }

    public Guid UserId { get; private set; }

    public string Remarks { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ReviewDraft Create(
        Guid loanApplicationId,
        Guid userId,
        string remarks,
        DateTimeOffset now)
    {
        Validate(remarks);
        return new ReviewDraft
        {
            Id = Guid.NewGuid(),
            LoanApplicationId = loanApplicationId,
            UserId = userId,
            Remarks = remarks,
            UpdatedAt = now
        };
    }

    public void Update(string remarks, DateTimeOffset now)
    {
        Validate(remarks);
        Remarks = remarks;
        UpdatedAt = now;
    }

    private static void Validate(string remarks)
    {
        ArgumentNullException.ThrowIfNull(remarks);
        if (remarks.Length > MaxRemarksLength)
        {
            throw new ArgumentException(
                $"Remarks must be {MaxRemarksLength} characters or fewer.",
                nameof(remarks));
        }
    }
}
