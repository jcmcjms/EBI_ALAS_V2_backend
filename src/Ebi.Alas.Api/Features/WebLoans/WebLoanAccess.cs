using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.WebLoans;

namespace Ebi.Alas.Api.Features.WebLoans;

/// <summary>
/// Branch isolation for CIS / LAI lookups. Encoders only see accounts for their home branch.
/// </summary>
public static class WebLoanAccess
{
    public static bool CanUseAccount(CallerContext caller, string? branchCode)
    {
        if (caller.CanAccessAllBranches)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(branchCode)
            && string.Equals(branchCode.Trim(), caller.BranchId, StringComparison.OrdinalIgnoreCase);
    }

    public static CisDetail FilterAccountsForCaller(CisDetail detail, CallerContext caller)
    {
        if (caller.CanAccessAllBranches)
        {
            return detail;
        }

        var accounts = detail.Accounts
            .Where(a => CanUseAccount(caller, a.BranchCode))
            .ToList();
        return detail with { Accounts = accounts };
    }
}
