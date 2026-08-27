using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Branches;

public class BranchService(IBranchRepository branchRepository) : IBranchService
{
    public async Task<PagedResult<BranchListResponse>> GetBranchesAsync(int pageNumber, int pageSize, bool? isActive = null)
    {
        return await branchRepository.GetBranchesAsync(pageNumber, pageSize, isActive);
    }

    public async Task<IReadOnlyList<BranchListResponse>> GetAllBranchesAsync(bool? isActive = null)
    {
        return await branchRepository.GetAllBranchesAsync(isActive);
    }

    public async Task<BranchResponse?> GetByIdAsync(int id)
    {
        var branch = await branchRepository.GetByIdAsync(id);
        if (branch == null)
            return null;

        return new BranchResponse(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.CreatedAt);
    }

    public async Task<BranchResponse?> GetByCodeAsync(string code)
    {
        var branch = await branchRepository.GetByCodeAsync(code);
        if (branch == null)
            return null;

        return new BranchResponse(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.CreatedAt);
    }
}