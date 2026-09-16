using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Branches;

namespace EBI.ALAS.Api.Features.ApprovalMatrix;

/// <summary>
/// Junction table for Branch-scope approvers who cover multiple branches.
/// When a Branch Head or OIC handles 2+ branches, rows here map them to each
/// covered branch. Area/Global scope approvers don't need this (their scope
/// is derived from Branch.AreaCode or unrestricted).
/// </summary>
public class UserBranchCoverage
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string BranchCode { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}
