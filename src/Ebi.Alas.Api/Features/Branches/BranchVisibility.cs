using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Branches;

public static class BranchVisibility
{
    public static bool IsAreaHead(string? jobTitle)
    {
        if (string.IsNullOrWhiteSpace(jobTitle))
        {
            return false;
        }

        var compact = new string(jobTitle.Where(char.IsLetterOrDigit).ToArray());
        return compact.Equals("AreaHead", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("AreaHeadLevelH", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Org-wide loan visibility for officers whose board authority is not branch-bound
    /// (RBG / Product / Credit Head, President, COO, CreCom).
    /// </summary>
    public static bool IsGlobalAuthority(string? jobTitle)
    {
        var level = Approvals.AuthorityJobTitles.Map(jobTitle);
        return level is Approvals.AuthorityLevel.RbgProductCredit
            or Approvals.AuthorityLevel.PresidentCoo
            or Approvals.AuthorityLevel.CreComBod;
    }

    public static async Task<IReadOnlyList<string>> GetReadableBranchIdsAsync(
        AlasDbContext db,
        Guid userId,
        UserRole role,
        string branchId,
        CancellationToken cancellationToken,
        string? jobTitle = null)
    {
        var home = (branchId ?? string.Empty).Trim();
        if (role is UserRole.Admin or UserRole.System or UserRole.Evaluator
            || IsGlobalAuthority(jobTitle))
        {
            return await db.Branches.AsNoTracking()
                .Select(b => b.Code)
                .ToListAsync(cancellationToken);
        }

        if (!IsAreaHead(jobTitle))
        {
            return string.IsNullOrEmpty(home) ? [] : [home];
        }

        if (string.IsNullOrEmpty(home))
        {
            return [];
        }

        var areaCode = await db.Branches.AsNoTracking()
            .Where(b => b.Code == home)
            .Select(b => b.AreaCode)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(areaCode))
        {
            return [home];
        }

        var codes = await db.Branches.AsNoTracking()
            .Where(b => b.AreaCode == areaCode)
            .Select(b => b.Code)
            .ToListAsync(cancellationToken);
        return codes.Contains(home) ? codes : codes.Append(home).ToList();
    }
}
