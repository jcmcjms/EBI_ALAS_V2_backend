using System.Security.Claims;
using System.Text.Json;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.Documents;
using Ebi.Alas.Api.Features.LoanApplications;
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
    Guid LoanApplicationId,
    string ChecklistIdCode,
    int? DocId,
    Guid? ParentRemarkId,
    Guid AuthorId,
    string AuthorName,
    string AuthorRole,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record CreateDocumentRemarkRequest(
    string ChecklistIdCode,
    int? DocId,
    Guid? ParentRemarkId,
    string Body);

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
            IChecklistDocumentReader checklistReader,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var loan = await LoanAccess.FindAccessibleAsync(db, id, caller, ct)
                ?? throw new Composition.Errors.NotFoundException("Loan", id.ToString());

            var productCode = loan.ProductCode;
            var formJson = loan.FormJson;

            var requirements = await db.LoanProductDocumentRequirements.AsNoTracking()
                .Where(r => r.ProductCode == productCode)
                .OrderBy(r => r.Code)
                .Select(r => r.Code)
                .ToListAsync(ct);

            var loanNo = ChecklistDocumentReader.ExtractLoanNo(formJson);
            if (loanNo is null)
            {
                return Results.Ok(Array.Empty<LoanChecklistDocumentDto>());
            }

            var documents = await checklistReader.GetChecklistDocumentsAsync(loanNo, requirements, ct);
            return Results.Ok(documents);
        })
        .WithName("GetChecklistDocuments");

        group.MapGet("/checklist-documents/{docId:int}/view", async (
            int docId,
            string? disposition,
            IChecklistDocumentReader checklistReader,
            CancellationToken ct) =>
        {
            var document = await checklistReader.GetDocumentFileAsync(docId, ct);
            if (document?.DocStr is null || document.DocStr.Length == 0)
            {
                throw new Composition.Errors.NotFoundException("Document", docId.ToString());
            }

            byte[] content;
            try
            {
                content = Convert.FromBase64String(document.DocStr);
            }
            catch (FormatException)
            {
                content = [];
            }

            if (content.Length == 0)
            {
                throw new Composition.Errors.NotFoundException("Document", docId.ToString());
            }

            var contentType = string.IsNullOrWhiteSpace(document.ContentType)
                ? "application/octet-stream"
                : document.ContentType;
            var wantsInline = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase)
                && InlineSafeTypes.Contains(contentType);
            var fileName = $"document-{docId}{FileExtension(contentType)}";
            return wantsInline
                ? Results.File(content, contentType)
                : Results.File(content, contentType, fileName);
        })
        .WithName("ViewChecklistDocument");

        group.MapGet("/{id:guid}/document-remarks", async (
            Guid id,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            await LoanAccess.RequireAccessibleAsync(db, id, caller, ct);

            var rows = await db.DocumentRemarks.AsNoTracking()
                .Where(r => r.LoanApplicationId == id)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(ct);

            var authorIds = rows.Select(r => r.AuthorId).Distinct().ToList();
            var authors = await db.Users.AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName, Role = u.Role.ToString() })
                .ToDictionaryAsync(u => u.Id, ct);

            return Results.Ok(rows.Select(r =>
            {
                authors.TryGetValue(r.AuthorId, out var author);
                return new DocumentRemarkDto(
                    r.Id,
                    r.LoanApplicationId,
                    r.ChecklistIdCode,
                    r.DocId,
                    r.ParentRemarkId,
                    r.AuthorId,
                    author?.Name ?? "Unknown",
                    author?.Role ?? string.Empty,
                    r.Body,
                    r.CreatedAt);
            }));
        })
        .WithName("GetDocumentRemarks");

        group.MapPost("/{id:guid}/document-remarks", async (
            Guid id,
            CreateDocumentRemarkRequest request,
            AlasDbContext db,
            DocumentRemarkService remarkService,
            ClaimsPrincipal user,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length < 2)
            {
                throw new Composition.Errors.ValidationException("Remark body is required.");
            }

            if (request.Body.Length > 2000)
            {
                throw new Composition.Errors.ValidationException("Remark body must not exceed 2000 characters.");
            }

            if (string.IsNullOrWhiteSpace(request.ChecklistIdCode))
            {
                throw new Composition.Errors.ValidationException("Checklist item code is required.");
            }

            var loan = await LoanAccess.RequireAccessibleAsync(db, id, caller, ct);
            _ = loan;

            var remark = await remarkService.CreateAsync(loan, request, caller.UserId, ct);

            var author = await db.Users.AsNoTracking()
                .Where(u => u.Id == caller.UserId)
                .Select(u => new { Name = u.FirstName + " " + u.LastName, Role = u.Role.ToString() })
                .FirstOrDefaultAsync(ct);

            return Results.Ok(new DocumentRemarkDto(
                remark.Id,
                remark.LoanApplicationId,
                remark.ChecklistIdCode,
                remark.DocId,
                remark.ParentRemarkId,
                remark.AuthorId,
                author?.Name ?? "Unknown",
                author?.Role ?? string.Empty,
                remark.Body,
                remark.CreatedAt));
        })
        .RequireRateLimiting("write")
        .WithName("CreateDocumentRemark");
    }

    private static string FileExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "application/pdf" => ".pdf",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/tiff" => ".tiff",
        "application/msword" => ".doc",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.ms-excel" => ".xls",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "text/plain" => ".txt",
        "text/html" => ".html",
        _ => ".bin",
    };
}
