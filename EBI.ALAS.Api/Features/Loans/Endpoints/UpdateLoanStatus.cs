using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.Presence;
using EBI.ALAS.Api.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

public static class UpdateLoanStatus
{
    public static void MapUpdateLoanStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapPut("/{id:int}/status", async (
            int id,
            [FromBody] UpdateLoanStatusRequest request,
            IValidator<UpdateLoanStatusRequest> validator,
            ILoanRepository loanRepository,
            ILoanWorkflowService workflowService,
            IAuditLogger auditLogger,
            INotificationService notificationService,
            IRealtimeNotificationService realtimeService,
            ClaimsPrincipal user,
            ITimeProvider timeProvider,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray());

                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "Validation failed",
                    errors.SelectMany(e => e.Value).ToList()));
            }

            var loan = await loanRepository.GetByIdAsync(id);
            if (loan == null)
            {
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            }

            var userRole = user.GetRole();
            var userId = user.GetUserId();

            if (!workflowService.IsValidTransition(loan.Status, request.Status, userRole))
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"Invalid status transition from {loan.Status} to {request.Status} for role {userRole}"));
            }

            var fromStatus = loan.Status;

            if (fromStatus == "Draft" && request.Status == "ForRecommendation")
            {
                var completenessService = ctx.RequestServices.GetRequiredService<IDocumentCompletenessService>();
                var completeness = await completenessService.CheckAsync(loan, ct);
                loan.DocumentsCompleteAt = completeness.Complete ? timeProvider.UtcNow : null;
            }

            if (request.Status == "ForApproval" && fromStatus == "ForChecking")
            {
                var completenessService = ctx.RequestServices.GetRequiredService<IDocumentCompletenessService>();
                var routingService = ctx.RequestServices.GetRequiredService<IApprovalRoutingService>();
                var assignmentService = ctx.RequestServices.GetRequiredService<ILoanAssignmentService>();

                var completeness = await completenessService.CheckAsync(loan, ct);
                if (!completeness.Complete)
                    return Results.Json(ApiResponse.ErrorResponse(
                        "Application cannot proceed to approval: incomplete documents.",
                        completeness.Missing.ToList()), statusCode: 422);

                loan.DocumentsCompleteAt = timeProvider.UtcNow;

                var decision = await routingService.RouteAsync(loan, ct);
                loan.DeviationSeverity = decision.Severity;
                loan.RequiredApprovalTier = decision.Tier;
                await loanRepository.UpdateAsync(loan);

                await assignmentService.AssignAsync(loan, ct);
            }

            if (fromStatus == "ForApproval" && userRole == Roles.Approver)
            {
                var db = ctx.RequestServices.GetRequiredService<AppDbContext>();
                var me = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId, ct);
                var inTier = me?.ApprovalAuthorityKey != null
                    && db.ApprovalAuthorities.Any(a => a.Key == me.ApprovalAuthorityKey
                        && a.Tier == loan.RequiredApprovalTier);
                var mineOrUnassigned = loan.AssignedApproverId is null || loan.AssignedApproverId == userId;
                if (!inTier || !mineOrUnassigned)
                    return Results.Json(ApiResponse.ErrorResponse(
                        $"This application requires a Tier {loan.RequiredApprovalTier} approver and must be assigned to you."),
                        statusCode: StatusCodes.Status403Forbidden);

                loan.AssignedApproverId = null;
                loan.AssignedAt = null;
            }

            var verdict = request.Verdict;
            if (fromStatus == "ForChecking" && request.Status == "ForApproval"
                && userRole == Roles.Evaluator)
            {
                if (verdict is not ("Recommended" or "NotRecommended"))
                    return Results.BadRequest(ApiResponse.ErrorResponse(
                        "An evaluation verdict ('Recommended' or 'NotRecommended') is required for this transition."));
            }
            else if (verdict is not null)
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "A verdict is only accepted on the evaluator's ForChecking → ForApproval transition."));
            }

            var actionName = (fromStatus, request.Status, verdict) switch
            {
                ("ForChecking", "ForApproval", "NotRecommended") => "EvaluatedNotRecommended",
                ("ForChecking", "ForApproval", "Recommended")    => "EvaluatedRecommended",
                (_, "ForRevision", _)                            => "PushedBack",
                _                                                => "StatusChanged",
            };

            loan.Status = request.Status;
            loan.LastActionDate = timeProvider.UtcNow;

            await loanRepository.UpdateAsync(loan);

            await auditLogger.LogActionAsync(
                id, userId, actionName, fromStatus, request.Status, request.Comments);

            var link = $"/loans/monitoring?id={id}";
            var actorName = $"{user.GetFirstName()} {user.GetLastName()}";
            var clientName = $"{loan.FirstName} {loan.LastName}";

            if (request.Status == "ForChecking")
            {
                var evaluators = await loanRepository.GetUsersByRoleAndBranchAsync(
                    Roles.Evaluator, loan.BranchCode, ct);
                foreach (var e in evaluators)
                {
                    await notificationService.CreateAsync(
                        e.Id,
                        "Ready for Evaluation",
                        $"{actorName} recommended {clientName}'s application ({loan.LamId}).",
                        link);

                    await realtimeService.NotifyUserAsync(
                        e.Id,
                        "Ready for Evaluation",
                        $"{actorName} recommended {clientName}'s application ({loan.LamId}).",
                        link);
                }
            }
            else if (request.Status == "ForRecommendation")
            {
                var recommenders = await loanRepository.GetUsersByRoleAndBranchAsync(
                    Roles.Recommender, loan.BranchCode, ct);
                foreach (var r in recommenders)
                {
                    await notificationService.CreateAsync(
                        r.Id,
                        "Ready for Recommendation",
                        $"{actorName} resubmitted {clientName}'s application ({loan.LamId}) for recommendation.",
                        link);

                    await realtimeService.NotifyUserAsync(
                        r.Id,
                        "Ready for Recommendation",
                        $"{actorName} resubmitted {clientName}'s application ({loan.LamId}) for recommendation.",
                        link);
                }
            }
            else if (request.Status == "ForApproval")
            {
                var approvers = await loanRepository.GetUsersByRoleAndBranchAsync(
                    Roles.Approver, loan.BranchCode, ct);
                var stance = verdict == "NotRecommended" ? "NOT RECOMMENDED" : "RECOMMENDED";
                var extra = verdict == "NotRecommended"
                    ? $" Evaluator remarks: {request.Comments}"
                    : string.Empty;
                foreach (var a in approvers)
                {
                    var title = verdict == "NotRecommended" ? "Evaluation: NOT Recommended" : "Ready for Approval";
                    var description = $"{actorName} evaluated {clientName}'s application ({loan.LamId}) as {stance}.{extra}";

                    await notificationService.CreateAsync(a.Id, title, description, link);

                    await realtimeService.NotifyUserAsync(a.Id, title, description, link);
                }
            }
            else if (request.Status == "ForRevision")
            {
                var pushbackRole = userRole == Roles.Recommender ? "Branch Head"
                                 : userRole == Roles.Approver ? "Area Head"
                                 : "Reviewer";

                var title = "Application Returned for Revision";
                var description = $"{pushbackRole} {actorName} returned {clientName}'s application ({loan.LamId}). Reason: {request.Comments}";

                await notificationService.CreateAsync(
                    loan.CreatedById,
                    title,
                    description,
                    link);

                await realtimeService.NotifyUserAsync(
                    loan.CreatedById,
                    title,
                    description,
                    link);
            }

            if (loan.CreatedById != userId)
            {
                var title = $"Status Update: {request.Status}";
                var description = $"Your application for {clientName} ({loan.LamId}) has been updated to {request.Status}.";

                await notificationService.CreateAsync(
                    loan.CreatedById,
                    title,
                    description,
                    link);

                await realtimeService.NotifyUserAsync(
                    loan.CreatedById,
                    title,
                    description,
                    link);
            }

            await realtimeService.NotifyDashboardUpdateAsync(loan.BranchCode);

            var response = new LoanResponse
            {
                Id = loan.Id,
                LamId = loan.LamId,
                ApplicationGroupNo = loan.ApplicationGroupNo,
                BranchCode = loan.BranchCode,
                LoanNo = loan.LoanNo,
                ProductCode = loan.ProductCode,
                Product = loan.Product,
                CisId = loan.CisId,
                FirstName = loan.FirstName,
                MiddleName = loan.MiddleName,
                LastName = loan.LastName,
                Agency = loan.Agency,
                Position = loan.Position,
                EmployeeId = loan.EmployeeId,
                NetTakeHomePay = loan.NetTakeHomePay,
                School = loan.School,
                Referrer = loan.Referrer,
                Purpose = loan.Purpose,
                ProposedAmount = loan.ProposedAmount,
                TermDays = loan.TermDays,
                InterestRate = loan.InterestRate,
                PolicyTermMonths = loan.PolicyTermMonths,
                ApprovalTermDays = loan.ApprovalTermDays,
                AnnualRatePercent = loan.AnnualRatePercent,
                Status = loan.Status,
                ApplicationDate = loan.ApplicationDate,
                LastActionDate = loan.LastActionDate,
                CreatedById = loan.CreatedById,
                CreatedByName = user.GetFirstName() + " " + user.GetLastName(),
                EvaluationVerdict = actionName.StartsWith("Evaluated", StringComparison.Ordinal)
                    ? actionName : null,
            };

            return Results.Ok(ApiResponse<LoanResponse>.SuccessResponse(response, "Loan status updated successfully"));
        })
        .WithName("UpdateLoanStatus")
        .Produces<ApiResponse<LoanResponse>>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(404);
    }
}

public class UpdateLoanStatusRequest
{
    public string Status { get; init; } = string.Empty;
    public string? Comments { get; init; }

    /// <summary>Evaluator-only verdict for ForChecking → ForApproval.
    /// Persisted as the audit action name so the approver sees the stance
    /// without a schema migration.</summary>
    public string? Verdict { get; init; }
}

public class UpdateLoanStatusValidator : AbstractValidator<UpdateLoanStatusRequest>
{
    private static readonly string[] ValidStatuses = new[]
    {
        "Draft", "ForRecommendation", "ForChecking", "ForApproval",
        "Approved", "Rejected", "ForRevision", "ForDisbursement",
        "Disbursed", "OnGoing",
        "Cancelled"
    };

    public UpdateLoanStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("Status is required")
            .Must(s => ValidStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}");

        RuleFor(x => x.Verdict)
            .Must(v => v is null or "Recommended" or "NotRecommended")
            .WithMessage("Verdict must be 'Recommended' or 'NotRecommended'.");

        When(x => x.Verdict == "NotRecommended"
                  || x.Status == "ForRevision"
                  || x.Status == "Rejected", () =>
        {
            RuleFor(x => x.Comments)
                .NotEmpty().MinimumLength(10)
                .WithMessage("Comments (min 10 characters) are required for pushbacks, rejections, and a Not Recommended evaluation.");
        });

        RuleFor(x => x.Comments).MaximumLength(2000)
            .WithMessage("Comments must not exceed 2000 characters");
    }
}
