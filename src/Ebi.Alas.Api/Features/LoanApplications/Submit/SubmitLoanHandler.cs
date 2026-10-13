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
    public async Task<LoanSubmissionResponse> HandleAsync(
        SubmitLoanApplicationsRequest request,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientName);
        if (request.Loans is null || request.Loans.Count == 0)
        {
            throw new ArgumentException("At least one loan is required.", nameof(request));
        }

        await workflow.EnsureLoadedAsync(cancellationToken);

        var branchId = caller.CanAccessAllBranches
            ? (string.IsNullOrWhiteSpace(request.BranchId) ? caller.BranchId : request.BranchId.Trim())
            : caller.BranchId;
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var now = timeProvider.GetUtcNow();
        var applicationGroupNo = string.IsNullOrWhiteSpace(request.ApplicationGroupNo)
            ? await numbers.GenerateGroupNumberAsync(cancellationToken)
            : request.ApplicationGroupNo.Trim();
        var lamIds = await numbers.GenerateLamIdsAsync(request.Loans.Count, cancellationToken);

        var created = new List<LoanApplication>(request.Loans.Count);
        for (var i = 0; i < request.Loans.Count; i++)
        {
            var line = request.Loans[i];
            ArgumentException.ThrowIfNullOrWhiteSpace(line.ProductCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(line.Product);

            var computed = computation.Compute(new LoanComputationInput(
                line.Principal,
                line.TermDays,
                line.InterestRatePerMonth,
                ServiceFee: line.ServiceFee,
                InsuranceFee: line.InsuranceFee,
                OtherDeductions: line.OtherDeductions));

            var loan = LoanApplication.Create(
                lamIds[i],
                applicationGroupNo,
                request.ClientName,
                branchId,
                request.LoanType,
                line.Principal,
                line.TermDays,
                line.InterestRatePerMonth,
                computed.TotalInterest,
                computed.TotalDeductions,
                computed.NetProceeds,
                caller.UserId,
                now,
                line.ProductCode,
                line.Product);

            if (!workflow.RequireRecommendation)
            {
                loan.TransitionTo(LoanStatus.ForChecking, now, caller.UserId, "Submitted");
            }

            db.LoanApplications.Add(loan);
            created.Add(loan);
        }

        await db.SaveChangesAsync(cancellationToken);

        var stage = workflow.RequireRecommendation
            ? WorkflowStage.Recommendation
            : WorkflowStage.Evaluation;
        foreach (var loan in created)
        {
            await queue.EnqueueAsync(loan.Id, stage, loan.BranchId, cancellationToken);
        }

        return new LoanSubmissionResponse(
            applicationGroupNo,
            created.Select(l => new CreatedLoanSummary(
                l.Id,
                l.LamId,
                l.ProductCode,
                l.Product,
                l.Principal,
                l.Status.ToString())).ToList());
    }
}
