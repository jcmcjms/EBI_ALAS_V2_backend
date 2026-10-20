using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.WorkflowActions;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

public sealed record GroupStatusRequest(string Status, string? Comments, Guid[]? LoanIds);

public sealed record GroupLoanResultDto(Guid LoanId, string LamId, bool Succeeded, string? Error);

public sealed record GroupStatusResponseDto(int Succeeded, int Failed, IReadOnlyList<GroupLoanResultDto> Results);

/// <summary>
/// Applies one workflow decision to every eligible loan of a client's bundle
/// (ApplicationGroupNo). Each loan transitions through the regular workflow
/// service so signatures, queue completion, notifications and history stay
/// identical to reviewing the loans one by one. Per-loan failures are reported
/// back instead of aborting the batch.
/// </summary>
public static class LoanGroupStatusHandler
{
    public static async Task<GroupStatusResponseDto> ApplyAsync(
        AlasDbContext db,
        LoanWorkflowService workflow,
        CallerContext caller,
        string groupNo,
        GroupStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupNo);
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.TryParse<LoanStatus>(request.Status, ignoreCase: false, out var target))
        {
            throw new ValidationException($"Unknown target status '{request.Status}'.");
        }

        if (target is not (LoanStatus.ForChecking or LoanStatus.ForApproval
            or LoanStatus.Approved or LoanStatus.Rejected or LoanStatus.ForRevision))
        {
            throw new ValidationException($"Target status '{target}' cannot be applied to a bundle.");
        }

        var query = db.LoanApplications.Where(l => l.ApplicationGroupNo == groupNo);
        if (!caller.CanViewAllLoanBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }

        var loans = await query.OrderBy(l => l.CreatedAt).ToListAsync(cancellationToken);
        if (loans.Count == 0)
        {
            throw new NotFoundException("Loan group", groupNo);
        }

        var selected = request.LoanIds is { Length: > 0 } ids
            ? loans.Where(l => ids.Contains(l.Id)).ToList()
            : loans;
        if (selected.Count == 0)
        {
            throw new ValidationException("No loans in this group match the selection.");
        }

        var comments = string.IsNullOrWhiteSpace(request.Comments) ? null : request.Comments.Trim();
        if (target == LoanStatus.ForRevision && comments is null)
        {
            throw new ValidationException("Comments are required when returning a bundle for revision.");
        }

        var results = new List<GroupLoanResultDto>(selected.Count);
        foreach (var loan in selected)
        {
            try
            {
                await TransitionAsync(workflow, loan.Id, target, comments, caller, cancellationToken);
                results.Add(new GroupLoanResultDto(loan.Id, loan.LamId, true, null));
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or ValidationException
                or ConflictException
                or ForbiddenException
                or NotFoundException)
            {
                results.Add(new GroupLoanResultDto(loan.Id, loan.LamId, false, ex.Message));
            }
        }

        var succeeded = results.Count(r => r.Succeeded);
        return new GroupStatusResponseDto(succeeded, results.Count - succeeded, results);
    }

    private static Task<LoanResponse> TransitionAsync(
        LoanWorkflowService workflow,
        Guid loanId,
        LoanStatus target,
        string? comments,
        CallerContext caller,
        CancellationToken cancellationToken) => target switch
    {
        LoanStatus.ForChecking => workflow.RecommendAsync(loanId, caller, cancellationToken, comments),
        LoanStatus.ForApproval => workflow.EvaluateAsync(loanId, recommend: true, caller, cancellationToken, comments),
        LoanStatus.Approved => workflow.ApproveAsync(loanId, caller, cancellationToken, comments),
        LoanStatus.Rejected => workflow.RejectAsync(loanId, caller, cancellationToken, comments),
        LoanStatus.ForRevision => workflow.PushBackAsync(loanId, caller, "General", comments!, cancellationToken),
        _ => throw new ValidationException($"Target status '{target}' cannot be applied to a bundle."),
    };
}
