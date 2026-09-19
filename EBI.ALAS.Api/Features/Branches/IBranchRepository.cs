using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Branches;

/// <summary>
/// Branch repository interface. Handles branch data access.
/// </summary>
public interface IBranchRepository
{
    Task<PagedResult<BranchListResponse>> GetBranchesAsync(int pageNumber, int pageSize, bool? isActive = null);
    Task<IReadOnlyList<BranchListResponse>> GetAllBranchesAsync(bool? isActive = null);
    Task<Branch?> GetByIdAsync(int id);
    Task<Branch?> GetByCodeAsync(string code);
    Task<Branch> CreateAsync(Branch branch);
    Task<Branch> UpdateAsync(Branch branch);
    Task<bool> DeleteAsync(int id);
}
