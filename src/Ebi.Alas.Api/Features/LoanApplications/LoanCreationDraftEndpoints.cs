using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

public sealed record LoanCreationDraftDto(string Payload, DateTimeOffset UpdatedAt);

public sealed record SaveLoanCreationDraftRequest(string? Payload);

/// <summary>
/// Per-officer auto-save draft for the loan-creation form. Private to the
/// authoring officer. Cleared after a successful submit or explicit clear.
/// </summary>
public static class LoanCreationDraftEndpoints
{
    public static void MapLoanCreationDrafts(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans").WithTags("Loans").RequireAuthorization();

        group.MapGet("/creation-draft", async (
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var draft = await FindAsync(db, caller.UserId, ct);
            return draft is null
                ? Results.NoContent()
                : Results.Ok(new LoanCreationDraftDto(draft.Payload, draft.UpdatedAt));
        })
        .WithName("GetLoanCreationDraft");

        group.MapPut("/creation-draft", async (
            SaveLoanCreationDraftRequest request,
            AlasDbContext db,
            ClaimsPrincipal user,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var payload = request.Payload ?? string.Empty;
            if (payload.Length > LoanCreationDraft.MaxPayloadLength)
            {
                throw new ValidationException(
                    $"Payload must be {LoanCreationDraft.MaxPayloadLength} characters or fewer.");
            }

            var now = timeProvider.GetUtcNow();
            var draft = await FindAsync(db, caller.UserId, ct);

            if (string.IsNullOrWhiteSpace(payload))
            {
                if (draft is not null)
                {
                    db.LoanCreationDrafts.Remove(draft);
                    await db.SaveChangesAsync(ct);
                }

                return Results.NoContent();
            }

            if (draft is null)
            {
                draft = LoanCreationDraft.Create(caller.UserId, payload, now);
                db.LoanCreationDrafts.Add(draft);
            }
            else
            {
                draft.Update(payload, now);
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new LoanCreationDraftDto(draft.Payload, draft.UpdatedAt));
        })
        .RequireRateLimiting("draft")
        .WithName("SaveLoanCreationDraft");

        group.MapDelete("/creation-draft", async (
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var draft = await FindAsync(db, caller.UserId, ct);
            if (draft is not null)
            {
                db.LoanCreationDrafts.Remove(draft);
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        })
        .RequireRateLimiting("write")
        .WithName("DeleteLoanCreationDraft");
    }

    private static Task<LoanCreationDraft?> FindAsync(
        AlasDbContext db,
        Guid userId,
        CancellationToken ct) =>
        db.LoanCreationDrafts.FirstOrDefaultAsync(d => d.UserId == userId, ct);
}
