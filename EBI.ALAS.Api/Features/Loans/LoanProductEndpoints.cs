using System.Security.Claims;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
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
        // Gated by CanViewLoanProduct so any of the five roles can
        // inspect the catalog. Retired products are included so the
        // admin screen can show "this product was retired on…" for
        // historical context — but the loan-creation form should
        // query /active instead.
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

        // ─── List active products only (loan-form dropdown source) ───
        // This is the endpoint the loan-creation form calls. Only
        // non-retired rows are returned so encoders can never pick
        // a product that webloan (the source of truth) considers
        // retired. The cached mirror means a hit here is one row
        // lookup, not a cross-database query.
        group.MapGet("/active", async (
            ILoanProductService service,
            CancellationToken ct) =>
        {
            var products = await service.GetActiveAsync(ct);
            return Results.Ok(
                ApiResponse<IReadOnlyList<LoanProductResponse>>.SuccessResponse(products));
        })
        .WithName("ListActiveLoanProducts")
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
    }
}
