using System.Security.Claims;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Models;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

public static class CreateLoan
{
    public static void MapCreateLoanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapPost("/", async (
            HttpContext http,
            [FromBody] SubmitLoanApplicationRequest request,
            IValidator<SubmitLoanApplicationRequest> validator,
            ILoanSubmissionService submissionService,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(http.Request.Headers["Idempotency-Key"].ToString(), out var idempotencyKey))
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    "A valid Idempotency-Key header (GUID) is required."));
            }

            var validationResult = await validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors
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
                var (response, replayed) = await submissionService.SubmitAsync(
                    request, idempotencyKey, user, ct);

                return replayed
                    ? Results.Ok(ApiResponse<LoanSubmissionResponse>.SuccessResponse(
                        response, "Submission replayed — Idempotency-Key already used."))
                    : Results.Created($"/api/loans/{response.Loans[0].Id}",
                        ApiResponse<LoanSubmissionResponse>.SuccessResponse(
                            response, "Application submitted for recommendation."));
            }
            catch (ForbiddenAccessException ex)
            {
                return Results.Json(
                    ApiResponse.ErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (InvalidWorkflowException ex)
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(ex.Message));
            }
        })
        .WithName("CreateLoanApplication")
        .Produces<ApiResponse<LoanSubmissionResponse>>(201)
        .Produces<ApiResponse<LoanSubmissionResponse>>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(403)
        .RequireAuthorization("CanCreateLoan");
    }
}
