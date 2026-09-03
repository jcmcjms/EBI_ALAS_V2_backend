using EBI.ALAS.Api.Common.Models;
using Microsoft.Extensions.Caching.Memory;

namespace EBI.ALAS.Api.Features.Branches;

public class BranchService : IBranchService
{
    // The branches list is genuinely static data — codes/names/active flags
    // change on the order of once a quarter. Cache it for an hour.
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
        // Paged requests stay un-cached: each (page, pageSize, isActive)
        // tuple is a separate request shape and most callers only ever
        // ask for page 1. The All endpoint is what gets cached.
        return await _branchRepository.GetBranchesAsync(pageNumber, pageSize, isActive);
    }

    public async Task<IReadOnlyList<BranchListResponse>> GetAllBranchesAsync(bool? isActive = null)
    {
        // The isActive filter is a single bool — there are only two
        // possible cache entries here, both bounded.
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