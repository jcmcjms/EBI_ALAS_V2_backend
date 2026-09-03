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
}
