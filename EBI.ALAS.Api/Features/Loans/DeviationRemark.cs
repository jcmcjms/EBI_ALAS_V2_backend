using EBI.ALAS.Api.Features.Auth;
namespace EBI.ALAS.Api.Features.Loans;
public class DeviationRemark
{
    public int Id { get; set; }
    public int LoanDeviationId { get; set; }
    public LoanDeviation Deviation { get; set; } = null!;
    public int? ParentRemarkId { get; set; }
    public DeviationRemark? ParentRemark { get; set; }
    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public string AuthorRole { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<DeviationRemark> Replies { get; set; } = new List<DeviationRemark>();
}
