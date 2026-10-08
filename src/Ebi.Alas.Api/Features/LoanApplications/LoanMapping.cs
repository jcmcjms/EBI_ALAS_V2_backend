using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Features.LoanApplications;

public static class LoanMapping
{
    public static LoanResponse ToResponse(this LoanApplication loan) => new(
        loan.Id,
        loan.LamId,
        loan.ApplicationGroupNo,
        loan.ClientName,
        loan.BranchId,
        loan.LoanType.ToString(),
        loan.Status.ToString(),
        loan.Principal,
        loan.TermDays,
        loan.InterestRate,
        loan.TotalInterest,
        loan.TotalDeductions,
        loan.NetProceeds,
        loan.CreatedAt);
}
