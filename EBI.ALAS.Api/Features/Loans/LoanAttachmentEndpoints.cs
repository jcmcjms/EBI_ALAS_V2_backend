using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public static class LoanAttachmentEndpoints
{
    private static readonly string[] AllowedExtensions =
        [".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx", ".xls", ".xlsx"];

    /// <summary>Workflow states after which the file set is frozen.</summary>
    private static readonly string[] TerminalStatuses =
        ["Approved", "Rejected", "Disbursed", "OnGoing"];

    public static void MapLoanAttachmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loan Attachments")
            .RequireAuthorization();

        // ── GET /api/loans/{id}/attachments ─────────────────────────────
        group.MapGet("/{id:int}/attachments", async (
            int id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id).Select(l => new { l.Id, l.CreatedById })
                .FirstOrDefaultAsync(ct);
            if (loan is null) return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            if (!CanRead(user, loan.CreatedById))
                return Results.Json(ApiResponse.ErrorResponse("You do not have permission to view this loan's files."),
                    statusCode: StatusCodes.Status403Forbidden);

            var items = await db.LoanAttachments.AsNoTracking()
                .Where(a => a.LoanApplicationId == id)
                .OrderByDescending(a => a.UploadedAt).ThenBy(a => a.Id)
                .Select(a => new LoanAttachmentResponse(
                    a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Category,
                    a.UploadedById,
                    $"{a.UploadedBy.FirstName} {a.UploadedBy.LastName}",
                    a.UploadedAt))
                .ToListAsync(ct);

            return Results.Ok(ApiResponse<List<LoanAttachmentResponse>>.SuccessResponse(items));
        })
        .WithName("GetLoanAttachments")
        .RequireAuthorization("CanViewLoan");

        // ── POST /api/loans/{id}/attachments (multipart) ────────────────
        group.MapPost("/{id:int}/attachments", async (
            int id, IFormFile file, string? category,
            ClaimsPrincipal user, AppDbContext db, IAuditLogger auditLogger,
            IConfiguration config, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var maxBytes = config.GetValue("LoanAttachments:MaxFileSizeBytes", 10 * 1024 * 1024);
            var maxFiles = config.GetValue("LoanAttachments:MaxFilesPerLoan", 25);

            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id).Select(l => new { l.Id, l.CreatedById, l.Status })
                .FirstOrDefaultAsync(ct);
            if (loan is null) return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            if (!CanRead(user, loan.CreatedById))
                return Results.Json(ApiResponse.ErrorResponse("You do not have permission to view this loan's files."),
                    statusCode: StatusCodes.Status403Forbidden);
            if (TerminalStatuses.Contains(loan.Status))
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"Files cannot be added once the application is {loan.Status}."));

            var userId = user.GetUserId();
            var isCreator = loan.CreatedById == userId;
            var isReviewer = user.HasPermission(Permissions.LoansRecommend)
                          || user.HasPermission(Permissions.LoansEvaluate)
                          || user.HasPermission(Permissions.LoansApprove);
            if (!isCreator && !isReviewer)
                return Results.Json(ApiResponse.ErrorResponse("Your role cannot attach files to this loan."),
                    statusCode: StatusCodes.Status403Forbidden);

            if (file is null || file.Length == 0)
                return Results.BadRequest(ApiResponse.ErrorResponse("No file was uploaded."));
            if (file.Length > maxBytes)
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"File exceeds the {maxBytes / (1024 * 1024)} MB limit."));

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"File type '{ext}' is not allowed. Allowed: {string.Join(", ", AllowedExtensions)}."));

            var count = await db.LoanAttachments.CountAsync(a => a.LoanApplicationId == id, ct);
            if (count >= maxFiles)
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"This loan already has the maximum of {maxFiles} files."));

            var root = ResolveStorageRoot(env, config);
            Directory.CreateDirectory(root);
            var storedName = $"{Guid.NewGuid():N}{ext}";
            var path = Path.Combine(root, storedName);

            await using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await file.CopyToAsync(fs, ct);
            }

            // Belt-and-braces: re-check the on-disk length, not just the client-declared one.
            var actual = new FileInfo(path).Length;
            if (actual > maxBytes)
            {
                File.Delete(path);
                return Results.BadRequest(ApiResponse.ErrorResponse("File exceeds the size limit."));
            }

            var attachment = new LoanAttachment
            {
                LoanApplicationId = id,
                FileName = Path.GetFileName(file.FileName),
                StoredFileName = storedName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream" : file.ContentType,
                SizeBytes = actual,
                Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                UploadedById = userId,
            };
            db.LoanAttachments.Add(attachment);
            await db.SaveChangesAsync(ct);

            await auditLogger.LogActionAsync(id, userId, "AttachmentAdded",
                null, null, $"Attached file '{attachment.FileName}'");

            return Results.Created($"/api/loans/{id}/attachments",
                ApiResponse<LoanAttachmentResponse>.SuccessResponse(new(
                    attachment.Id, attachment.FileName, attachment.ContentType,
                    attachment.SizeBytes, attachment.Category, attachment.UploadedById,
                    user.GetFirstName() + " " + user.GetLastName(), attachment.UploadedAt),
                    "File uploaded."));
        })
        .DisableAntiforgery()
        .WithName("UploadLoanAttachment")
        .RequireAuthorization("CanViewLoan");

        // ── GET /api/loans/attachments/{attachmentId}/download ──────────
        group.MapGet("/attachments/{attachmentId:int}/download", async (
            int attachmentId, ClaimsPrincipal user, AppDbContext db,
            IConfiguration config, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var att = await db.LoanAttachments.AsNoTracking()
                .Include(a => a.LoanApplication)
                .FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
            if (att is null) return Results.NotFound(ApiResponse.ErrorResponse("Attachment not found"));
            if (!CanRead(user, att.LoanApplication.CreatedById))
                return Results.Json(ApiResponse.ErrorResponse("You do not have permission to download this file."),
                    statusCode: StatusCodes.Status403Forbidden);

            var path = Path.Combine(ResolveStorageRoot(env, config), att.StoredFileName);
            if (!File.Exists(path))
                return Results.NotFound(ApiResponse.ErrorResponse("File is missing from storage."));

            return Results.File(File.OpenRead(path), att.ContentType, att.FileName);
        })
        .WithName("DownloadLoanAttachment")
        .RequireAuthorization("CanViewLoan");

        // ── DELETE /api/loans/attachments/{attachmentId} ────────────────
        group.MapDelete("/attachments/{attachmentId:int}", async (
            int attachmentId, ClaimsPrincipal user, AppDbContext db, IAuditLogger auditLogger,
            IConfiguration config, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var att = await db.LoanAttachments.Include(a => a.LoanApplication)
                .FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
            if (att is null) return Results.NotFound(ApiResponse.ErrorResponse("Attachment not found"));

            var userId = user.GetUserId();
            var isAdmin = string.Equals(user.GetRole(), Roles.Admin, StringComparison.Ordinal);
            if (att.UploadedById != userId && !isAdmin)
                return Results.Json(ApiResponse.ErrorResponse("Only the uploader (or an administrator) can delete this file."),
                    statusCode: StatusCodes.Status403Forbidden);
            if (TerminalStatuses.Contains(att.LoanApplication.Status))
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"Files cannot be removed once the application is {att.LoanApplication.Status}."));

            db.LoanAttachments.Remove(att);
            await db.SaveChangesAsync(ct);

            var path = Path.Combine(ResolveStorageRoot(env, config), att.StoredFileName);
            if (File.Exists(path)) File.Delete(path);

            await auditLogger.LogActionAsync(att.LoanApplicationId, userId, "AttachmentRemoved",
                null, null, $"Removed file '{att.FileName}'");

            return Results.Ok(ApiResponse.SuccessResponse("File deleted."));
        })
        .WithName("DeleteLoanAttachment")
        .RequireAuthorization("CanViewLoan");
    }

    private static bool CanRead(ClaimsPrincipal user, int createdById) =>
        user.HasPermission(Permissions.LoansView) || user.GetUserId() == createdById;

    private static string ResolveStorageRoot(IWebHostEnvironment env, IConfiguration config)
    {
        var relative = config["LoanAttachments:StoragePath"] ?? "App_Data/loan-attachments";
        return Path.GetFullPath(Path.Combine(env.ContentRootPath, relative));
    }
}

public sealed record LoanAttachmentResponse(
    int Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Category,
    int UploadedById,
    string UploadedByName,
    DateTime UploadedAt);
