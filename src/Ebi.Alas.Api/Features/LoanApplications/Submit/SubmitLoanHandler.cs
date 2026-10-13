using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanComputation;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;

namespace Ebi.Alas.Api.Features.LoanApplications.Submit;

public sealed class SubmitLoanHandler(
    AlasDbContext db,
    LoanComputationService computation,
    WorkflowQueueService queue,
    IWorkflowConfiguration workflow,
    ApplicationNumberGenerator numbers,
    TimeProvider timeProvider)
{
    public async Task<LoanResponse> HandleAsync(
        SubmitLoanRequest request,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProductCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Product);

        await workflow.EnsureLoadedAsync(cancellationToken);

        var branchId = caller.CanAccessAllBranches
            ? (string.IsNullOrWhiteSpace(request.BranchId) ? caller.BranchId : request.BranchId.Trim())
            : caller.BranchId;
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var computed = computation.Compute(new LoanComputationInput(
            request.Principal,
            request.TermDays,
            request.InterestRatePerMonth,
            ServiceFee: request.ServiceFee,
            InsuranceFee: request.InsuranceFee,
            OtherDeductions: request.OtherDeductions));

        var now = timeProvider.GetUtcNow();
        var applicationGroupNo = string.IsNullOrWhiteSpace(request.ApplicationGroupNo)
            ? await numbers.GenerateGroupNumberAsync(cancellationToken)
            : request.ApplicationGroupNo.Trim();
        var lamId = await numbers.GenerateLamIdAsync(cancellationToken);

        var loan = LoanApplication.Create(
            lamId,
            applicationGroupNo,
            request.ClientName,
            branchId,
            request.LoanType,
            request.Principal,
            request.TermDays,
            request.InterestRatePerMonth,
            computed.TotalInterest,
            computed.TotalDeductions,
            computed.NetProceeds,
            caller.UserId,
            now,
            request.ProductCode,
            request.Product);

        if (!workflow.RequireRecommendation)
        {
            loan.TransitionTo(LoanStatus.ForChecking, now, caller.UserId, "Submitted");
        }

        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync(cancellationToken);

        var stage = workflow.RequireRecommendation
            ? WorkflowStage.Recommendation
            : WorkflowStage.Evaluation;
        await queue.EnqueueAsync(loan.Id, stage, loan.BranchId, cancellationToken);
        return LoanMapping.ToResponse(loan);
    }
}
