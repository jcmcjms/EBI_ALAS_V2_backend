using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Branches;

public interface IBranchService
{
    Task<PagedResult<BranchListResponse>> GetBranchesAsync(int pageNumber, int pageSize, bool? isActive = null);
    Task<IReadOnlyList<BranchListResponse>> GetAllBranchesAsync(bool? isActive = null);
    Task<BranchResponse?> GetByIdAsync(int id);
    Task<BranchResponse?> GetByCodeAsync(string code);
}