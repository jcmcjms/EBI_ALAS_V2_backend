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
    TimeProvider timeProvider)
{
    public async Task<LoanResponse> HandleAsync(
        SubmitLoanRequest request,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApplicationGroupNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientName);

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
        var lamId = $"LAM-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32];
        var loan = LoanApplication.Create(
            lamId,
            request.ApplicationGroupNo.Trim(),
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
            now);

        if (!workflow.RequireRecommendation)
        {
            loan.TransitionTo(LoanStatus.ForChecking, now);
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
