using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Branches;

public class BranchRepository(AppDbContext context) : IBranchRepository
{
    public async Task<PagedResult<BranchListResponse>> GetBranchesAsync(int pageNumber, int pageSize, bool? isActive = null)
    {
        var query = context.Branches.AsNoTracking();

        if (isActive.HasValue)
        {
            query = query.Where(b => b.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(b => b.Code)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new BranchListResponse(b.Id, b.Code, b.Name, b.IsActive))
            .ToListAsync();

        return PagedResult<BranchListResponse>.Create(items, totalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyList<BranchListResponse>> GetAllBranchesAsync(bool? isActive = null)
    {
        var query = context.Branches.AsNoTracking();

        if (isActive.HasValue)
        {
            query = query.Where(b => b.IsActive == isActive.Value);
        }

        return await query
            .OrderBy(b => b.Code)
            .Select(b => new BranchListResponse(b.Id, b.Code, b.Name, b.IsActive))
            .ToListAsync();
    }

    public async Task<Branch?> GetByIdAsync(int id)
    {
        return await context.Branches.FindAsync(id);
    }

    public async Task<Branch?> GetByCodeAsync(string code)
    {
        return await context.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Code == code);
    }

    public async Task<Branch> CreateAsync(Branch branch)
    {
        context.Branches.Add(branch);
        await context.SaveChangesAsync();
        return branch;
    }

    public async Task<Branch> UpdateAsync(Branch branch)
    {
        context.Branches.Update(branch);
        await context.SaveChangesAsync();
        return branch;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var branch = await context.Branches.FindAsync(id);
        if (branch == null)
            return false;

        context.Branches.Remove(branch);
        await context.SaveChangesAsync();
        return true;
    }
}