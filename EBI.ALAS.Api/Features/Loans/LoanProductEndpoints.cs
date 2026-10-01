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
                var userId = user.GetUserId();
                var updated = await service.UpdateAsync(code, request, userId, ct);
                if (updated is not null)
                {
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
