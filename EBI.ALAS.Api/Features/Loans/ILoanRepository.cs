using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Loans;
public interface ILoanRepository
{
    Task<LoanApplication?> GetByIdAsync(int id, bool includeRelated = false);
    Task<LoanApplication?> GetByFormNumberAsync(string formNumber);
    Task<PagedResult<LoanApplication>> GetAllAsync(int page, int pageSize, string? role = null, string? branchId = null, int? userId = null);
    Task<LoanApplication> CreateAsync(LoanApplication loan);
    Task UpdateAsync(LoanApplication loan);
    Task<bool> ExistsAsync(int id);
    Task<int> GetCountByStatusAsync(string status, string? branchId = null);
    Task<decimal> GetTotalAmountByStatusAsync(string status, string? branchId = null);
}
