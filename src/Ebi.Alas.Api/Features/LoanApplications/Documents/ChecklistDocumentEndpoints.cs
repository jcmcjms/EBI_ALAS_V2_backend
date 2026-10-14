using System.Security.Claims;
using System.Text.Json;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.WebLoans;
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
    public static void MapChecklistDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans").WithTags("Documents").RequireAuthorization();

        group.MapGet("/{id:guid}/checklist-documents", async (
            Guid id,
            AlasDbContext db,
            IWebLoanReader webLoan,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new { l.Id, l.LamId, l.ProductCode, l.BranchId, l.FormJson })
                .FirstOrDefaultAsync(ct);
            if (loan is null)
            {
                throw new Composition.Errors.NotFoundException("Loan", id.ToString());
            }

            if (!caller.CanAccessAllBranches && loan.BranchId != caller.BranchId)
            {
                throw new Composition.Errors.NotFoundException("Loan", id.ToString());
            }

            var requirements = await db.LoanProductDocumentRequirements.AsNoTracking()
                .Where(r => r.ProductCode == loan.ProductCode)
                .OrderBy(r => r.Code)
                .Select(r => r.Code)
                .ToListAsync(ct);

            var cisNo = ExtractCisId(loan.FormJson);
            var webloanItems = cisNo is null
                ? Array.Empty<WebLoanChecklistRow>()
                : await webLoan.GetChecklistItemsAsync(cisNo, ct);
            var catalog = await webLoan.GetChecklistCatalogAsync(requirements, ct);
            var descriptionByCode = catalog
                .Where(c => !string.IsNullOrWhiteSpace(c.Description))
                .GroupBy(c => c.IdCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Description, StringComparer.OrdinalIgnoreCase);

            var documents = requirements.Select(code =>
            {
                var match = webloanItems.FirstOrDefault(w =>
                    string.Equals(w.CheckListItem, code, StringComparison.OrdinalIgnoreCase));
                var uploaded = match?.Submitted is not null;
                // Reference: checklist_description comes only from webloan check_list_all.
                descriptionByCode.TryGetValue(code, out var checklistDescription);
                return new LoanChecklistDocumentDto(
                    LoanNo: loan.LamId,
                    LoanProduct: loan.ProductCode,
                    IdCode: code,
                    ChecklistDescription: checklistDescription,
                    DocId: null,
                    DocStr: null,
                    MiniStr: null,
                    ContentType: null,
                    Created: match?.Submitted,
                    UploadedBy: null,
                    UploadStatus: uploaded ? "Uploaded" : "No uploaded documents");
            }).ToList();

            return Results.Ok(documents);
        })
        .WithName("GetChecklistDocuments");

        group.MapGet("/checklist-documents/{docId:int}/view", (
            int docId) =>
        {
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

    public static string? ExtractCisId(string? formJson)
    {
        if (string.IsNullOrWhiteSpace(formJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(formJson);
            if (doc.RootElement.TryGetProperty("client", out var client)
                && client.ValueKind == JsonValueKind.Object
                && client.TryGetProperty("cisId", out var cisId)
                && cisId.ValueKind == JsonValueKind.String)
            {
                var value = cisId.GetString()?.Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
