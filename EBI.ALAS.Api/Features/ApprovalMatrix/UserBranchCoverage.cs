using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.Branches;
namespace EBI.ALAS.Api.Features.ApprovalMatrix;
public class UserBranchCoverage
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string BranchCode { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}
