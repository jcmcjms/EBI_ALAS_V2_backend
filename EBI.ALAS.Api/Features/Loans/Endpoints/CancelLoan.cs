using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Features.Presence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

public static class CancelLoan
{
    public static void MapCancelLoanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapPost("/{id:int}/cancel", async (
            int id,
            [FromBody] CancelLoanApplicationRequest request,
            IValidator<CancelLoanApplicationRequest> validator,
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
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.BadRequest(ApiResponse.ErrorResponse("Validation failed",
                    validation.Errors.Select(e => e.ErrorMessage).ToList()));

            var loan = await loanRepository.GetByIdAsync(id);
            if (loan is null) return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            var userId = user.GetUserId();
            var userRole = user.GetRole();

            var isAdmin = string.Equals(userRole, Roles.Admin, StringComparison.Ordinal);
            if (loan.CreatedById != userId && !isAdmin)
                return Results.Json(ApiResponse.ErrorResponse(
                    "Only the encoder who created this application can cancel it."),
                    statusCode: StatusCodes.Status403Forbidden);

            if (!workflowService.IsValidTransition(loan.Status, "Cancelled", userRole))
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"This application is {loan.Status} and cannot be cancelled. " +
                    "Cancellation is only allowed from Draft, ForRecommendation, " +
                    "ForChecking, ForApproval, or ForRevision."));

            var fromStatus = loan.Status;
            loan.Status = "Cancelled";
            loan.LastActionDate = timeProvider.UtcNow;
            await loanRepository.UpdateAsync(loan);

            // ── Queue lifecycle: dequeue from the review desk ──
            var queueService = ctx.RequestServices.GetRequiredService<IWorkflowQueueService>();
            await queueService.DequeueAndPromoteAsync(loan, fromStatus, ct);

            await auditLogger.LogActionAsync(id, userId, "ApplicationCancelled",
                fromStatus, "Cancelled", request.Reason);

            var actorName = $"{user.GetFirstName()} {user.GetLastName()}";
            var clientName = $"{loan.FirstName} {loan.LastName}";
            var link = $"/loans/monitoring?id={id}";

            string? notifyRole = fromStatus switch
            {
                "ForRecommendation" => Roles.Recommender,
                "ForChecking"       => Roles.Evaluator,
                "ForApproval"       => Roles.Approver,
                _                   => null,
            };

            if (notifyRole is not null)
            {
                var recipients = await loanRepository.GetUsersByRoleAndBranchAsync(notifyRole, loan.BranchCode, ct);
                foreach (var r in recipients)
                {
                    var title = "Application Cancelled";
                    var description = $"{actorName} cancelled {clientName}'s application ({loan.LamId}). Reason: {request.Reason}";

                    await notificationService.CreateAsync(r.Id, title, description, link);

                    await realtimeService.NotifyUserAsync(r.Id, title, description, link);
                }
            }

            if (loan.CreatedById != userId)
            {
                var title = "Application Cancelled";
                var description = $"An administrator cancelled your application for {clientName} ({loan.LamId}). Reason: {request.Reason}";

                await notificationService.CreateAsync(loan.CreatedById, title, description, link);

                await realtimeService.NotifyUserAsync(loan.CreatedById, title, description, link);
            }

            return Results.Ok(ApiResponse.SuccessResponse(
                $"Application cancelled. The {clientName} file has been closed."));
        })
        .WithName("CancelLoanApplication")
        .Produces<ApiResponse>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(403)
        .Produces<ApiResponse>(404)
        .RequireAuthorization("CanViewLoan");
    }
}

public sealed record CancelLoanApplicationRequest
{
    public string Reason { get; init; } = string.Empty;
}

public sealed class CancelLoanApplicationValidator : AbstractValidator<CancelLoanApplicationRequest>
{
    public CancelLoanApplicationValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason for cancellation is required.")
            .MinimumLength(10).WithMessage("Reason must be at least 10 characters.")
            .MaximumLength(2000).WithMessage("Reason must not exceed 2000 characters.");
    }
}
