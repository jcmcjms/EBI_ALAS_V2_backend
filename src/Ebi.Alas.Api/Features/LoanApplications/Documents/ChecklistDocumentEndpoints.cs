using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Documents;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.Documents;

public sealed record LoanChecklistDocumentDto(
    string LoanNo,
    string LoanProduct,
    string IdCode,
    string? ChecklistDescription,
    int? DocId,
    string? DocStr,
    string? MiniStr,
    string? ContentType,
    DateTimeOffset? Created,
    string? UploadedBy,
    string UploadStatus);

public sealed record DocumentRemarkDto(
    Guid Id,
    string ChecklistIdCode,
    Guid? DocId,
    string Body,
    string AuthorName,
    DateTimeOffset CreatedAt);

public static class ChecklistDocumentEndpoints
{
    private static readonly HashSet<string> InlineSafeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "image/jpg", "image/gif",
    };

    public static void MapChecklistDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans").WithTags("Documents").RequireAuthorization();

        group.MapGet("/{id:guid}/checklist-documents", async (
            Guid id,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new { l.Id, l.LamId, l.ProductCode, l.BranchId })
                .FirstOrDefaultAsync(ct);
            if (loan is null)
            {
                throw new Composition.Errors.NotFoundException("Loan", id.ToString());
            }

            if (!caller.CanAccessAllBranches && loan.BranchId != caller.BranchId)
            {
                throw new Composition.Errors.NotFoundException("Loan", id.ToString());
            }

            var items = await db.DocumentChecklistItems.AsNoTracking()
                .Where(d => d.LoanApplicationId == id)
                .OrderBy(d => d.Code)
                .Select(d => new
                {
                    d.Code,
                    d.Name,
                    d.IsSubmitted,
                    d.IsVerified
                })
                .ToListAsync(ct);

            var documents = items.Select(item => new LoanChecklistDocumentDto(
                LoanNo: loan.LamId,
                LoanProduct: loan.ProductCode,
                IdCode: item.Code,
                ChecklistDescription: item.Name,
                DocId: item.IsSubmitted ? null : null,
                DocStr: null,
                MiniStr: null,
                ContentType: null,
                Created: null,
                UploadedBy: null,
                UploadStatus: item.IsSubmitted ? "Uploaded" : "No uploaded documents")).ToList();

            return Results.Ok(documents);
        })
        .WithName("GetChecklistDocuments");

        group.MapGet("/checklist-documents/{docId:int}/view", async (
            int docId,
            string? disposition,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            _ = disposition;
            _ = db;
            _ = user;
            _ = ct;
            // Binary document store is not wired in this API yet.
            throw new Composition.Errors.NotFoundException("Document", docId.ToString());
        })
        .WithName("ViewChecklistDocument");

        group.MapGet("/{id:guid}/document-remarks", async (
            Guid id,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var exists = await db.LoanApplications.AsNoTracking()
                .AnyAsync(l => l.Id == id, ct);
            if (!exists)
            {
                throw new Composition.Errors.NotFoundException("Loan", id.ToString());
            }

            _ = caller;
            return Results.Ok(Array.Empty<DocumentRemarkDto>());
        })
        .WithName("GetDocumentRemarks");
    }
}
