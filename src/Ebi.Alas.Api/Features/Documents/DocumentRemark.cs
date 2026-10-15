namespace Ebi.Alas.Api.Features.Documents;

public sealed class DocumentRemark
{
    private DocumentRemark()
    {
        ChecklistIdCode = string.Empty;
        Body = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid LoanApplicationId { get; private set; }

    public string ChecklistIdCode { get; private set; }

    public int? DocId { get; private set; }

    public Guid? ParentRemarkId { get; private set; }

    public string Body { get; private set; }

    public Guid AuthorId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DocumentRemark Create(
        Guid loanApplicationId,
        string checklistIdCode,
        int? docId,
        Guid? parentRemarkId,
        string body,
        Guid authorId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checklistIdCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        return new DocumentRemark
        {
            Id = Guid.NewGuid(),
            LoanApplicationId = loanApplicationId,
            ChecklistIdCode = checklistIdCode.Trim(),
            DocId = docId,
            ParentRemarkId = parentRemarkId,
            Body = body.Trim(),
            AuthorId = authorId,
            CreatedAt = now
        };
    }
}
