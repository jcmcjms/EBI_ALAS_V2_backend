using EBI.ALAS.Api.Shared.Models;
using Microsoft.Extensions.Caching.Memory;
namespace EBI.ALAS.Api.Features.Branches;
public class BranchService : IBranchService
{
    private static readonly TimeSpan AllBranchesTtl = TimeSpan.FromHours(1);
    private const string AllBranchesCacheKey = "branches:all";
    private readonly IBranchRepository _branchRepository;
    private readonly IMemoryCache _cache;
    public BranchService(IBranchRepository branchRepository, IMemoryCache cache)
    {
        _branchRepository = branchRepository;
        _cache = cache;
    }
    public async Task<PagedResult<BranchListResponse>> GetBranchesAsync(int pageNumber, int pageSize, bool? isActive = null)
    {
        return await _branchRepository.GetBranchesAsync(pageNumber, pageSize, isActive);
    }
    public async Task<IReadOnlyList<BranchListResponse>> GetAllBranchesAsync(bool? isActive = null)
    {
        var cacheKey = $"{AllBranchesCacheKey}:{(isActive.HasValue ? isActive.Value.ToString().ToLowerInvariant() : "any")}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<BranchListResponse>? cached) && cached is not null)
        {
            return cached;
        }
        var result = await _branchRepository.GetAllBranchesAsync(isActive);
        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = AllBranchesTtl,
            Size = 1
        });
        return result;
    }
    public async Task<BranchResponse?> GetByIdAsync(int id)
    {
        var branch = await _branchRepository.GetByIdAsync(id);
        if (branch == null)
            return null;
        return new BranchResponse(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.CreatedAt);
    }
    public async Task<BranchResponse?> GetByCodeAsync(string code)
    {
        var branch = await _branchRepository.GetByCodeAsync(code);
        if (branch == null)
            return null;
        return new BranchResponse(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.CreatedAt);
    }
}
