using System.Text.Json.Nodes;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications.Documents;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanComputation;
using Ebi.Alas.Api.Features.LoanProducts;
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
    IChecklistDocumentReader checklistDocuments,
    IRealtimeNotifier? realtime,
    TimeProvider timeProvider)
{
    public async Task<SubmitLoanResult> HandleAsync(
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
        NthpDateGuard.EnsureAcceptable(
            request.Form,
            DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));
        var missingDocuments = await FindMissingRequiredDocumentsAsync(request, cancellationToken);
        if (missingDocuments.Count > 0)
        {
            return SubmitLoanResult.Blocked(missingDocuments);
        }
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
            for (var i = 0; i < created.Count; i++)
            {
                created[i].SetFormSnapshot(ScopeFormToLoan(formSnapshot, i));
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
            await SignatureChainService.EnsureCreatedAsync(
                db, loan.Id, workflow.RequireRecommendation, now, cancellationToken);
            await SignatureChainService.SignAsync(
                db, loan.Id, "Encoder", caller.UserId, now, cancellationToken);
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

        return SubmitLoanResult.Success(new LoanSubmissionResponse(
            applicationGroupNo,
            created.Select(l => new CreatedLoanSummary(
                l.Id,
                l.LamId,
                l.ProductCode,
                l.Product,
                l.Principal,
                l.Status.ToString())).ToList()));
    }

    private async Task<IReadOnlyList<string>> FindMissingRequiredDocumentsAsync(
        SubmitLoanApplicationsRequest request,
        CancellationToken cancellationToken)
    {
        var productCodes = request.Loans
            .Select(l => l.ProductCode.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var requirements = await db.LoanProductDocumentRequirements.AsNoTracking()
            .Where(r => productCodes.Contains(r.ProductCode))
            .Select(r => new { r.ProductCode, r.LoanType, r.Code, r.Description })
            .ToListAsync(cancellationToken);

        if (requirements.Count == 0)
        {
            return [];
        }

        var requiredByProductAndType = requirements
            .GroupBy(r => (Product: r.ProductCode, Type: r.LoanType))
            .ToDictionary(
                g => (Product: g.Key.Product.Trim(), Type: g.Key.Type),
                g => g.Select(x => new { x.Code, x.Description }).ToList());

        var formJson = request.Form is { } form ? form.GetRawText() : null;
        var missing = new List<string>();

        for (var i = 0; i < request.Loans.Count; i++)
        {
            var line = request.Loans[i];
            var loanType = ChecklistDocumentReader.ResolveLoanTypeAt(formJson, i, request.LoanType);
            if (!requiredByProductAndType.TryGetValue((line.ProductCode.Trim(), loanType), out var required)
                || required.Count == 0)
            {
                continue;
            }

            var loanNo = ChecklistDocumentReader.ExtractLoanNoAt(formJson, i);
            if (string.IsNullOrWhiteSpace(loanNo))
            {
                missing.AddRange(required.Select(r => $"{r.Description} is missing"));
                continue;
            }

            var codes = required.Select(r => r.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var docs = await checklistDocuments.GetChecklistDocumentsAsync(loanNo, codes, cancellationToken);
            var uploaded = docs
                .Where(d => string.Equals(d.UploadStatus, "Uploaded", StringComparison.Ordinal))
                .Select(d => d.IdCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var r in required)
            {
                if (!uploaded.Contains(r.Code))
                {
                    missing.Add($"{r.Description} is missing");
                }
            }
        }

        return missing;
    }

    public static string BuildMissingDocumentsMessage(IReadOnlyList<string> missing) =>
        "Cannot submit the application. Required documents are missing:\n"
        + string.Join("\n", missing.Select(m => $"• {m}"))
        + "\nUpload them in the checklist, then try again.";

    private static string ScopeFormToLoan(System.Text.Json.JsonElement form, int loanIndex)
    {
        var root = JsonNode.Parse(form.GetRawText())!.AsObject();
        if (root["loans"] is JsonArray loans
            && loanIndex >= 0
            && loanIndex < loans.Count)
        {
            root["loans"] = new JsonArray(loans[loanIndex]?.DeepClone());
        }

        return root.ToJsonString();
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
            now,
            link);
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
