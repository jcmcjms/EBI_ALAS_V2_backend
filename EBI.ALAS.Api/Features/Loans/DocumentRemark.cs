using EBI.ALAS.Api.Features.Auth;
namespace EBI.ALAS.Api.Features.Loans;
public class DocumentRemark
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public LoanApplication LoanApplication { get; set; } = null!;
    public string ChecklistIdCode { get; set; } = string.Empty;
    public int? DocId { get; set; }
    public int? ParentRemarkId { get; set; }
    public DocumentRemark? ParentRemark { get; set; }
    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public string AuthorRole { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<DocumentRemark> Replies { get; set; } = new List<DocumentRemark>();
}
