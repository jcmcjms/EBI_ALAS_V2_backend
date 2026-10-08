namespace Ebi.Alas.Api.Features.Revisions;

public sealed class RevisionRequest
{
    private RevisionRequest()
    {
        Section = string.Empty;
        Comment = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid LoanApplicationId { get; private set; }

    public string Section { get; private set; }

    public string Comment { get; private set; }

    public Guid RequestedBy { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public static RevisionRequest Create(
        Guid loanApplicationId,
        string section,
        string comment,
        Guid requestedBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        return new RevisionRequest
        {
            Id = Guid.NewGuid(),
            LoanApplicationId = loanApplicationId,
            Section = section.Trim(),
            Comment = comment.Trim(),
            RequestedBy = requestedBy,
            RequestedAt = now
        };
    }

    public void Resolve(DateTimeOffset now)
    {
        ResolvedAt = now;
    }

    public bool IsResolved => ResolvedAt is not null;
}
