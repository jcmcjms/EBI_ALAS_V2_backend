using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

public sealed record ReviewDraftDto(string Remarks, DateTimeOffset UpdatedAt);

public sealed record SaveReviewDraftRequest(string? Remarks);

/// <summary>
/// Per-reviewer auto-save draft for an application's review remarks.
/// Drafts are private to the authoring officer and are cleared server-side
/// when the loan transitions to another stage.
/// </summary>
public static class ReviewDraftEndpoints
{
    public static void MapReviewDrafts(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans").WithTags("Loans").RequireAuthorization();

        group.MapGet("/{id:guid}/review-draft", async (
            Guid id,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            await LoanAccess.RequireAccessibleAsync(db, id, caller, ct);

            var draft = await FindAsync(db, id, caller.UserId, ct);
            return draft is null
                ? Results.NoContent()
                : Results.Ok(new ReviewDraftDto(draft.Remarks, draft.UpdatedAt));
        })
        .WithName("GetReviewDraft");

        group.MapPut("/{id:guid}/review-draft", async (
            Guid id,
            SaveReviewDraftRequest request,
            AlasDbContext db,
            ClaimsPrincipal user,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            await LoanAccess.RequireAccessibleAsync(db, id, caller, ct);

            var remarks = request.Remarks?.Trim() ?? string.Empty;
            if (remarks.Length > ReviewDraft.MaxRemarksLength)
            {
                throw new ValidationException(
                    $"Remarks must be {ReviewDraft.MaxRemarksLength} characters or fewer.");
            }

            var now = timeProvider.GetUtcNow();
            var draft = await FindAsync(db, id, caller.UserId, ct);

            if (remarks.Length == 0)
            {
                if (draft is not null)
                {
                    db.ReviewDrafts.Remove(draft);
                    await db.SaveChangesAsync(ct);
                }

                return Results.NoContent();
            }

            if (draft is null)
            {
                draft = ReviewDraft.Create(id, caller.UserId, remarks, now);
                db.ReviewDrafts.Add(draft);
            }
            else
            {
                draft.Update(remarks, now);
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new ReviewDraftDto(draft.Remarks, draft.UpdatedAt));
        })
        .RequireRateLimiting("draft")
        .WithName("SaveReviewDraft");

        group.MapDelete("/{id:guid}/review-draft", async (
            Guid id,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            await LoanAccess.RequireAccessibleAsync(db, id, caller, ct);

            var draft = await FindAsync(db, id, caller.UserId, ct);
            if (draft is not null)
            {
                db.ReviewDrafts.Remove(draft);
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        })
        .RequireRateLimiting("write")
        .WithName("DeleteReviewDraft");
    }

    private static Task<ReviewDraft?> FindAsync(
        AlasDbContext db,
        Guid loanApplicationId,
        Guid userId,
        CancellationToken ct) =>
        db.ReviewDrafts.FirstOrDefaultAsync(
            d => d.LoanApplicationId == loanApplicationId && d.UserId == userId,
            ct);
}
