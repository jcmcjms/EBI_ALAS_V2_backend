using EBI.ALAS.Api.Features.Auth;
namespace EBI.ALAS.Api.Features.Loans;
public class LoanSubmissionIdempotency
{
    public int Id { get; set; }
    public Guid IdempotencyKey { get; set; }
    public int UserId { get; set; }
    public string ResponseJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public User User { get; set; } = null!;
}
