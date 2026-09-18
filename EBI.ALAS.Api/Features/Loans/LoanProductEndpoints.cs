using System.Security.Claims;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.AuditLogs;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Loans;

public static class LoanProductEndpoints
{
    public static void MapLoanProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loan-products")
            .WithTags("LoanProducts");

        // ─── List all products (admin view, includes retired) ─────────
        // Gated by CanViewLoanProduct so admins can inspect the
        // catalog. Retired products are included so the admin screen
        // can show "this product was retired on…" for historical
        // context. The creation tree no longer calls this endpoint —
        // it uses the pending-loan feed and `/loan-class` instead.
        group.MapGet("/", async (
            ILoanProductService service,
            CancellationToken ct) =>
        {
            var products = await service.GetAllAsync(ct);
            return Results.Ok(
                ApiResponse<IReadOnlyList<LoanProductResponse>>.SuccessResponse(products));
        })
        .WithName("ListLoanProducts")
        .Produces<ApiResponse<IReadOnlyList<LoanProductResponse>>>(200)
        .Produces<ApiResponse>(401)
        .RequireAuthorization("CanViewLoanProduct");

        // ─── Get one product by code (admin edit form) ───────────────
        // 404 when the code is not in the mirror — typically means
        // the sync hasn't run yet for that webloan code. Endpoint
        // returns the full record including IsRetired + LastSyncedAt
        // so the admin form can show the staleness indicator.
        group.MapGet("/{code}", async (
            string code,
            ILoanProductService service,
            CancellationToken ct) =>
        {
            var product = await service.GetByCodeAsync(code, ct);
            return product is null
                ? Results.NotFound(
                    ApiResponse.ErrorResponse($"Loan product '{code}' not found."))
                : Results.Ok(ApiResponse<LoanProductResponse>.SuccessResponse(product));
        })
        .WithName("GetLoanProductByCode")
        .Produces<ApiResponse<LoanProductResponse>>(200)
        .Produces<ApiResponse>(404)
        .Produces<ApiResponse>(401)
        .RequireAuthorization("CanViewLoanProduct");

        // ─── Update policy fields (Admin only) ───────────────────────
        // Edits the policy fields only. The sync-owned fields
        // (IsRetired, Description, Code, LastSyncedAt) are preserved
        // — this endpoint cannot retire a product, change its code,
        // or rewrite its description. Those are all driven by the
        // webloan sync.
        //
        // Returns 400 with validation errors if the policy fields
        // violate business rules (FluentValidation). Returns 404 if
        // the code is not in the mirror — run a sync first.
        group.MapPut("/{code}", async (
            string code,
            [FromBody] UpdateLoanProductRequest request,
            IValidator<UpdateLoanProductRequest> validator,
            ILoanProductService service,
            IAuditLogService auditLogService,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                var errors = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "Validation failed",
                    errors.SelectMany(e => e.Value).ToList()));
            }

            try
            {
                // Resolve the caller's User.Id from the JWT claim so
                // the row's UpdatedById carries the human attribution.
                // RequireAuthorization("CanManageLoanProduct") has
                // already gated this endpoint to admins, so the
                // resolved id is trustworthy. If the claim is
                // somehow missing (shouldn't happen post-authz),
                // GetUserId() returns 0 — the repository will then
                // save a row attributed to user 0, which the admin
                // grid will surface as an obvious anomaly rather
                // than silently misattribute.
                var userId = user.GetUserId();
                var updated = await service.UpdateAsync(code, request, userId, ct);
                if (updated is not null)
                {
                    // CUD audit: capture the product-policy edit so the
                    // admin's "Audit Logs" page reflects the change.
                    await auditLogService.LogAsync(
                        userId,
                        $"{user.GetFirstName()} {user.GetLastName()}",
                        "Update", "LoanProduct", code, updated.Description,
                        $"Updated policy fields for loan product {code}");
                }
                return updated is null
                    ? Results.NotFound(
                        ApiResponse.ErrorResponse($"Loan product '{code}' not found."))
                    : Results.Ok(ApiResponse<LoanProductResponse>.SuccessResponse(
                        updated, "Loan product updated successfully."));
            }
            catch (ArgumentException ex)
            {
                // Service-side defense-in-depth check fired (e.g.
                // validator was bypassed by an internal caller).
                return Results.BadRequest(
                    ApiResponse.ErrorResponse(ex.Message));
            }
        })
        .WithName("UpdateLoanProduct")
        .Produces<ApiResponse<LoanProductResponse>>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(404)
        .Produces<ApiResponse>(401)
        .RequireAuthorization("CanManageLoanProduct");

        // ─── Manual sync trigger (Admin only) ────────────────────────
        // The hosted background service runs the same sync on its
        // interval; this endpoint exists for ops to force a refresh
        // without waiting for the next tick (useful after a
        // webloan-side change that needs to be visible immediately,
        // and during incident response).
        //
        // Returns the summary so the admin UI can show
        // "Synced 7 products: 1 added, 1 retired, 5 preserved".
        group.MapPost("/sync", async (
            ILoanProductService service,
            CancellationToken ct) =>
        {
            var result = await service.SyncFromWebloanAsync(ct);
            return Results.Ok(ApiResponse<LoanProductSyncResult>.SuccessResponse(
                result, "Loan product sync completed."));
        })
        .WithName("SyncLoanProducts")
        .Produces<ApiResponse<LoanProductSyncResult>>(200)
        .Produces<ApiResponse>(401)
        .RequireAuthorization("CanManageLoanProduct");

        // ─── Export products to Excel (View permission) ─────────────
        // Downloads the full product catalog as an .xlsx file. Retired
        // products are included by default so ops can see the full
        // history; pass IncludeRetired=false to get only active ones.
        group.MapGet("/export", async (
            [AsParameters] ExportLoanProductsParameters parameters,
            ILoanProductImportService importService,
            CancellationToken ct) =>
        {
            var bytes = await importService.ExportAsync(parameters, ct);
            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"loan-products-export-{DateTime.UtcNow:yyyyMMdd}.xlsx");
        })
        .WithName("ExportLoanProducts")
        .Produces(200)
        .Produces<ApiResponse>(401)
        .RequireAuthorization("CanViewLoanProduct");

        // ─── Download import template (Manage permission) ───────────
        // Returns a blank .xlsx template with headers, example rows,
        // and an instructions sheet describing the import rules.
        group.MapGet("/import/template", async (
            ILoanProductImportService importService,
            CancellationToken ct) =>
        {
            var bytes = await importService.GenerateTemplateAsync(ct);
            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "loan-product-import-template.xlsx");
        })
        .WithName("GetLoanProductImportTemplate")
        .Produces(200)
        .Produces<ApiResponse>(401)
        .RequireAuthorization("CanManageLoanProduct");

        // ─── Import products from Excel (Manage permission) ─────────
        // Accepts an .xlsx file and upserts loan products. Existing
        // codes are updated (policy fields + Description + IsRetired);
        // new codes are created with LastSyncedAt = MinValue so the
        // sync can pick them up on the next run.
        //
        // Returns a summary with created/updated counts and per-row
        // validation errors. The caller should show the error report
        // so ops can fix the spreadsheet and re-upload.
        group.MapPost("/import", async (
            IFormFile file,
            ILoanProductImportService importService,
            IAuditLogService auditLogService,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (file == null || file.Length == 0)
                return Results.BadRequest(ApiResponse.ErrorResponse("No file uploaded"));

            if (file.Length > 10 * 1024 * 1024)
                return Results.BadRequest(ApiResponse.ErrorResponse("File size exceeds 10 MB limit"));

            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(ApiResponse.ErrorResponse("Only .xlsx files are supported"));

            using var stream = file.OpenReadStream();
            var result = await importService.ImportAsync(stream, user.GetUserId(), $"{user.GetFirstName()} {user.GetLastName()}", ct);

            await auditLogService.LogAsync(
                user.GetUserId(),
                $"{user.GetFirstName()} {user.GetLastName()}",
                "Import", "LoanProductBatch",
                result.TotalRows.ToString(),
                $"{result.Created} created, {result.Updated} updated",
                $"Imported {result.Created + result.Updated} products ({result.Failed} failed)");

            return Results.Ok(ApiResponse<LoanProductImportResult>.SuccessResponse(
                result,
                $"Imported {result.Created + result.Updated} products ({result.Created} created, {result.Updated} updated)"));
        })
        .WithName("ImportLoanProducts")
        .Produces<ApiResponse<LoanProductImportResult>>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(401)
        .RequireAuthorization("CanManageLoanProduct")
        .DisableAntiforgery();
    }
}
