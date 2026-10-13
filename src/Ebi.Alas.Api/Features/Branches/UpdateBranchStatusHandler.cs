using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Branches;

public static class BranchLoanOpenStatus
{
    public static bool IsOpen(LoanStatus status) =>
        status is not LoanStatus.Rejected and not LoanStatus.Cancelled;
}

public sealed class BranchCloseBlockedException(string code, int openLoans, int activeUsers)
    : Exception($"Branch '{code}' cannot be closed or merged while {openLoans} open loan(s) and {activeUsers} active user(s) remain.")
{
    public string Code { get; } = code;
    public int OpenLoans { get; } = openLoans;
    public int ActiveUsers { get; } = activeUsers;
}

public sealed class UpdateBranchStatusHandler(AlasDbContext db)
{
    public async Task<BranchResponse> HandleAsync(
        string code,
        UpdateBranchStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.TryParse<BranchStatus>(request.Status, ignoreCase: true, out var next))
        {
            throw new ArgumentException("Status must be Active, Closed, or Merged.", nameof(request));
        }

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Code == code, cancellationToken)
            ?? throw new NotFoundException("Branch", code);

        var leavingActive = branch.Status == BranchStatus.Active
            && next is BranchStatus.Closed or BranchStatus.Merged;

        if (leavingActive)
        {
            var openLoans = await db.LoanApplications
                .CountAsync(l => l.BranchId == code && BranchLoanOpenStatus.IsOpen(l.Status), cancellationToken);
            var activeUsers = await db.Users
                .CountAsync(u => u.BranchId == code && u.Status == UserStatus.Active, cancellationToken);

            if (openLoans > 0 || activeUsers > 0)
            {
                throw new BranchCloseBlockedException(code, openLoans, activeUsers);
            }
        }

        branch.ChangeStatus(next, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return branch.ToResponse();
    }
}
