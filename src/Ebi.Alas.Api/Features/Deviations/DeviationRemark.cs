namespace Ebi.Alas.Api.Features.Deviations;

public sealed class DeviationRemark
{
    private DeviationRemark()
    {
        Body = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid LoanDeviationId { get; private set; }

    public Guid? ParentRemarkId { get; private set; }

    public string Body { get; private set; }

    public Guid AuthorId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DeviationRemark Create(
        Guid loanDeviationId,
        Guid? parentRemarkId,
        string body,
        Guid authorId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        return new DeviationRemark
        {
            Id = Guid.NewGuid(),
            LoanDeviationId = loanDeviationId,
            ParentRemarkId = parentRemarkId,
            Body = body.Trim(),
            AuthorId = authorId,
            CreatedAt = now
        };
    }
}
