using EBI.ALAS.Api.Features.Auth;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Replay guard for POST /api/loans. One row per (IdempotencyKey, UserId):
/// a retried submit carrying the same key returns the stored response
/// instead of minting a second application group (and a second LAM series).
/// </summary>
public class LoanSubmissionIdempotency
{
    public int Id { get; set; }
    public Guid IdempotencyKey { get; set; }
    public int UserId { get; set; }

    /// <summary>Serialized LoanSubmissionResponse returned on replay.</summary>
    public string ResponseJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}