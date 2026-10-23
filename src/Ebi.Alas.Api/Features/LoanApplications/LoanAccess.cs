using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

/// <summary>
/// Branch isolation for loan reads/writes. Visibility follows board authority
/// (branch / area / global) and always includes loans assigned to the caller.
/// </summary>
public static class LoanAccess
{
    public static async Task<LoanApplication> RequireAccessibleAsync(
        AlasDbContext db,
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken,
        bool tracked = false)
    {
        return await FindAccessibleAsync(db, loanId, caller, cancellationToken, tracked)
            ?? throw new NotFoundException("Loan", loanId.ToString());
    }

    public static async Task<bool> CanAccessAsync(
        AlasDbContext db,
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
        => await FindAccessibleAsync(db, loanId, caller, cancellationToken) is not null;

    /// <summary>
    /// Restrict a loan query to the caller's readable branches plus any loan
    /// assigned to them (shared by list/group endpoints).
    /// </summary>
    public static async Task<IQueryable<LoanApplication>> FilterAccessibleAsync(
        AlasDbContext db,
        IQueryable<LoanApplication> query,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        if (caller.CanViewAllLoanBranches)
        {
            return query;
        }

        var jobTitle = await db.Users.AsNoTracking()
            .Where(u => u.Id == caller.UserId)
            .Select(u => u.JobTitle)
            .FirstOrDefaultAsync(cancellationToken);

        if (await Branches.BranchVisibility.IsGlobalAuthorityAsync(db, caller.UserId, jobTitle, cancellationToken))
        {
            return query;
        }

        var readable = await Branches.BranchVisibility.GetReadableBranchIdsAsync(
            db,
            caller.UserId,
            caller.Role,
            caller.BranchId,
            cancellationToken,
            jobTitle);

        return query.Where(l =>
            readable.Contains(l.BranchId) || l.AssignedToUserId == caller.UserId);
    }

    public static async Task<LoanApplication?> FindAccessibleAsync(
        AlasDbContext db,
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken,
        bool tracked = false)
    {
        var query = (tracked ? db.LoanApplications : db.LoanApplications.AsNoTracking())
            .Where(l => l.Id == loanId);
        if (caller.CanViewAllLoanBranches)
        {
            return await query.FirstOrDefaultAsync(cancellationToken);
        }

        // Always visible to the assignee (including unassigned shared-pool work
        // via authority scope below).
        var assigned = await query
            .Where(l => l.AssignedToUserId == caller.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (assigned is not null)
        {
            return assigned;
        }

        var jobTitle = await db.Users.AsNoTracking()
            .Where(u => u.Id == caller.UserId)
            .Select(u => u.JobTitle)
            .FirstOrDefaultAsync(cancellationToken);

        if (await Branches.BranchVisibility.IsGlobalAuthorityAsync(db, caller.UserId, jobTitle, cancellationToken))
        {
            return await query.FirstOrDefaultAsync(cancellationToken);
        }

        var readable = await Branches.BranchVisibility.GetReadableBranchIdsAsync(
            db,
            caller.UserId,
            caller.Role,
            caller.BranchId,
            cancellationToken,
            jobTitle);

        return await query
            .Where(l => readable.Contains(l.BranchId))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
