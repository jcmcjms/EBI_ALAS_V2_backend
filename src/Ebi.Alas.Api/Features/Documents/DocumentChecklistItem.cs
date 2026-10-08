namespace Ebi.Alas.Api.Features.Documents;

public enum DocumentFlag
{
    Complete = 0,
    Incomplete = 1
}

public sealed class DocumentChecklistItem
{
    private DocumentChecklistItem()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid LoanApplicationId { get; private set; }

    public string Name { get; private set; }

    public bool IsSubmitted { get; private set; }

    public bool IsVerified { get; private set; }

    public DocumentFlag Flag { get; private set; }

    public static DocumentChecklistItem Create(Guid loanApplicationId, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new DocumentChecklistItem
        {
            Id = Guid.NewGuid(),
            LoanApplicationId = loanApplicationId,
            Name = name.Trim(),
            IsSubmitted = false,
            IsVerified = false,
            Flag = DocumentFlag.Incomplete
        };
    }

    public void Submit(DateTimeOffset now)
    {
        IsSubmitted = true;
    }

    public void Verify(DateTimeOffset now)
    {
        if (!IsSubmitted)
        {
            throw new InvalidOperationException("Cannot verify an unsubmitted document.");
        }

        IsVerified = true;
        Flag = DocumentFlag.Complete;
    }

    public void MarkIncomplete()
    {
        Flag = DocumentFlag.Incomplete;
        IsVerified = false;
    }
}
