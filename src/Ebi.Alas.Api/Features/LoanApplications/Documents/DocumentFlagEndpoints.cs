using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.Documents;

public sealed record FlagDocumentsRequest(
    IReadOnlyList<string> MissingRequirementCodes,
    string Reason);

public sealed class DocumentFlagHandler(
    AlasDbContext db,
    IRealtimeNotifier? realtime,
    TimeProvider timeProvider)
{
    public async Task FlagAsync(
        Guid loanId,
        FlagDocumentsRequest request,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var codes = (request.MissingRequirementCodes ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToList();
        if (codes.Count == 0)
        {
            throw new ArgumentException("At least one missing requirement code is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
        {
            throw new ArgumentException("A reason of at least 5 characters is required when flagging documents.");
        }
        if (request.Reason.Length > 2000)
        {
            throw new ArgumentException("Reason must not exceed 2000 characters.");
        }

        var loan = await db.LoanApplications
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken)
            ?? throw new Composition.Errors.NotFoundException("Loan", loanId.ToString());

        EnsureCanFlag(loan, caller);

        var now = timeProvider.GetUtcNow();
        loan.FlagDocuments(codes.Count, request.Reason, caller.UserId, now);

        var missingList = string.Join(", ", codes);
        db.Notifications.Add(Notification.Create(
            loan.CreatedBy,
            "Documents Flagged — Upload Required",
            $"A reviewer flagged {loan.LamId} as lacking documents. Missing: {missingList}. Reason: {request.Reason.Trim()}.",
            NotificationType.Action,
            now));

        await db.SaveChangesAsync(cancellationToken);

        if (realtime is not null)
        {
            await realtime.NotifyUserAsync(
                Guid.NewGuid(),
                loan.CreatedBy,
                "Documents Flagged — Upload Required",
                $"Missing: {missingList}.",
                $"/loans/review/{loan.Id}",
                now,
                cancellationToken);
        }
    }

    public async Task ClearAsync(
        Guid loanId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var loan = await db.LoanApplications
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken)
            ?? throw new Composition.Errors.NotFoundException("Loan", loanId.ToString());

        if (loan.DocumentsFlaggedAt is null)
        {
            throw new ArgumentException("This loan has no active document flag.");
        }

        var isFlagger = loan.DocumentsFlaggedById == caller.UserId;
        var isEncoder = loan.CreatedBy == caller.UserId;
        if (caller.Role != UserRole.Admin && !isFlagger && !isEncoder)
        {
            throw new Composition.Errors.ForbiddenException(
                "Only the flagger, the encoder, or an admin can clear the document flag.");
        }

        var now = timeProvider.GetUtcNow();
        loan.ClearDocumentFlag(now);
        db.Notifications.Add(Notification.Create(
            loan.CreatedBy,
            "Document Flag Cleared",
            $"The document flag on {loan.LamId} has been cleared.",
            NotificationType.Action,
            now));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureCanFlag(Domain.LoanApplication loan, CallerContext caller)
    {
        if (caller.Role == UserRole.Admin)
        {
            return;
        }

        if (!caller.CanAccessAllBranches && loan.BranchId != caller.BranchId)
        {
            throw new Composition.Errors.ForbiddenException("You cannot flag documents for this branch.");
        }

        var required = DeskRoleFor(loan.Status);
        if (required is null || required != caller.Role)
        {
            throw new Composition.Errors.ForbiddenException(
                $"Only a {required?.ToString().ToLowerInvariant() ?? "reviewer"} can flag documents at the {loan.Status} desk.");
        }
    }

    private static UserRole? DeskRoleFor(Domain.LoanStatus status) => status switch
    {
        Domain.LoanStatus.ForRecommendation => UserRole.Recommender,
        Domain.LoanStatus.ForChecking => UserRole.Evaluator,
        Domain.LoanStatus.ForApproval => UserRole.Approver,
        _ => null,
    };
}

public static class DocumentFlagEndpoints
{
    public static void MapDocumentFlagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans/{id:guid}/document-flag")
            .WithTags("Documents")
            .RequireAuthorization();

        group.MapPost("/", async (
            Guid id,
            FlagDocumentsRequest request,
            DocumentFlagHandler handler,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            try
            {
                await handler.FlagAsync(id, request, caller, ct);
                return Results.Ok(new { message = "Documents flagged." });
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: ex.Message);
            }
        })
        .WithName("FlagDocuments");

        group.MapDelete("/", async (
            Guid id,
            DocumentFlagHandler handler,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            try
            {
                await handler.ClearAsync(id, caller, ct);
                return Results.Ok(new { message = "Document flag cleared." });
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: ex.Message);
            }
        })
        .WithName("ClearDocumentFlag");
    }
}
