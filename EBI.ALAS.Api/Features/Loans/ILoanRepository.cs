using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Loans;
public interface ILoanRepository
{
    Task<LoanApplication?> GetByIdAsync(int id, bool includeRelated = false, CancellationToken ct = default);
    Task<LoanApplication?> GetByFormNumberAsync(string formNumber, CancellationToken ct = default);
    Task<PagedResult<LoanApplication>> GetAllAsync(
        int page,
        int pageSize,
        string? role = null,
        string? branchId = null,
        int? userId = null,
        bool includeRelated = false,
        CancellationToken ct = default);
    Task<LoanApplication> CreateAsync(LoanApplication loan, CancellationToken ct = default);
    Task UpdateAsync(LoanApplication loan, CancellationToken ct = default);
    Task<bool> ExistsAsync(int id, CancellationToken ct = default);
    Task<int> GetCountByStatusAsync(string status, string? branchId = null, CancellationToken ct = default);
    Task<decimal> GetTotalAmountByStatusAsync(string status, string? branchId = null, CancellationToken ct = default);

    // ── Multi-loan submission (POST /api/loans) ──
    /// <summary>Display name resolved from the Users table; null when the user is missing.</summary>
    Task<string?> GetOfficerDisplayNameAsync(int userId, CancellationToken ct = default);

    /// <summary>Stored replay response for a (key, user) pair, or null on first submit.</summary>
    Task<LoanSubmissionIdempotency?> GetIdempotencyRecordAsync(Guid key, int userId, CancellationToken ct = default);

    /// <summary>Atomically persists the N applications and the idempotency row in one transaction.</summary>
    Task CreateSubmissionAsync(IReadOnlyList<LoanApplication> applications, LoanSubmissionIdempotency idempotency, CancellationToken ct = default);

    /// <summary>Updates the serialized response on the idempotency row after the PKs are known.</summary>
    Task UpdateIdempotencyResponseAsync(LoanSubmissionIdempotency idempotency, CancellationToken ct = default);
}