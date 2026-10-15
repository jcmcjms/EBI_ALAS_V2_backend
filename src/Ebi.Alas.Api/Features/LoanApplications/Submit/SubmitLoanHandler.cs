using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanComputation;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.Submit;

public sealed class SubmitLoanHandler(
    AlasDbContext db,
    LoanComputationService computation,
    WorkflowQueueService queue,
    IWorkflowConfiguration workflow,
    ApplicationNumberGenerator numbers,
    ApplicationArrivalNotifier arrivals,
    IRealtimeNotifier? realtime,
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

        if (request.Form is { } formSnapshot)
        {
            var formJson = formSnapshot.GetRawText();
            foreach (var loan in created)
            {
                loan.SetFormSnapshot(formJson);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var loan in created)
        {
            await Deviations.LoanDeviationSync.SyncFromFormAsync(db, loan, loan.FormJson, cancellationToken);
        }

        var stage = workflow.RequireRecommendation
            ? WorkflowStage.Recommendation
            : WorkflowStage.Evaluation;
        foreach (var loan in created)
        {
            await queue.EnqueueAsync(loan.Id, stage, loan.BranchId, cancellationToken);
        }

        var audienceRole = workflow.RequireRecommendation
            ? UserRole.Recommender
            : UserRole.Evaluator;
        var audienceTitle = workflow.RequireRecommendation
            ? "New application ready for recommendation"
            : "New application ready for evaluation";
        var encoderTitle = workflow.RequireRecommendation
            ? "Application submitted for recommendation"
            : "Application submitted for evaluation";
        var firstLoanId = created[0].Id;
        var approvalLink = $"/loans/approval/{firstLoanId}";
        foreach (var branch in created.Select(l => l.BranchId).Distinct())
        {
            var branchLamIds = created
                .Where(l => l.BranchId == branch)
                .Select(l => l.LamId)
                .ToList();
            await arrivals.NotifyRoleAsync(
                audienceRole,
                branch,
                audienceTitle,
                $"Loan application(s) {string.Join(", ", branchLamIds)} submitted by {request.ClientName}.",
                link: approvalLink,
                cancellationToken);
        }

        await NotifyUserAsync(
            caller.UserId,
            encoderTitle,
            $"Application {string.Join(", ", created.Select(l => l.LamId))} was submitted and is awaiting review.",
            approvalLink,
            cancellationToken);

        if (realtime is not null)
        {
            await realtime.NotifyDashboardUpdatedAsync(cancellationToken);
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

    private async Task NotifyUserAsync(
        Guid userId,
        string title,
        string body,
        string? link,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var notification = Notification.Create(
            userId,
            title,
            body,
            NotificationType.Application,
            now);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);
        if (realtime is null)
        {
            return;
        }

        await realtime.NotifyUserAsync(
            notification.Id,
            userId,
            title,
            body,
            link,
            notification.CreatedAt,
            cancellationToken);
    }
}
