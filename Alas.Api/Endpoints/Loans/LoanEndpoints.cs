using System.Security.Claims;
using Alas.Api.Validation;
using Alas.Application.Common.Security;
using Alas.Application.Admin.Users;
using Alas.Application.Loans;
using Alas.Domain.Entities;
using Alas.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Alas.Api.Endpoints.Loans;

public static class LoanEndpoints
{
    public static IEndpointRouteBuilder MapLoanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans");

        group.MapGet("/", ListLoansAsync)
            .RequireAuthorization(AlasPermissions.LoansRead)
            .Produces<PagedResult<LoanListItemDto>>();

        group.MapGet("/monitor", GetMonitorAsync)
            .RequireAuthorization(AlasPermissions.LoansMonitor)
            .Produces<LoanMonitorDto>();

        group.MapGet("/{id:guid}", GetLoanAsync)
            .RequireAuthorization(AlasPermissions.LoansRead)
            .Produces<LoanDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateLoanAsync)
            .RequireAuthorization(AlasPermissions.LoansCreate)
            .AddEndpointFilter<ValidationFilter<CreateLoanRequest>>()
            .Produces<LoanDetailDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/submit", SubmitForReviewAsync)
            .RequireAuthorization(AlasPermissions.LoansCreate)
            .Produces<LoanDetailDto>()
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/submit-approval", SubmitForApprovalAsync)
            .RequireAuthorization(AlasPermissions.LoansApprove)
            .Produces<LoanDetailDto>()
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/approve", ApproveLoanAsync)
            .RequireAuthorization(AlasPermissions.LoansApprove)
            .AddEndpointFilter<ValidationFilter<ApproveLoanRequest>>()
            .Produces<LoanDetailDto>()
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/reject", RejectLoanAsync)
            .RequireAuthorization(AlasPermissions.LoansApprove)
            .AddEndpointFilter<ValidationFilter<RejectLoanRequest>>()
            .Produces<LoanDetailDto>()
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> ListLoansAsync(
        [AsParameters] LoanQueryParams queryParams,
        LoanService loanService,
        CancellationToken cancellationToken)
    {
        var result = await loanService.ListAsync(
            queryParams.Page,
            queryParams.PageSize,
            queryParams.Search,
            queryParams.Status,
            queryParams.BranchId,
            cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetMonitorAsync(
        LoanService loanService,
        CancellationToken cancellationToken)
    {
        var monitor = await loanService.GetMonitorAsync(cancellationToken);
        return Results.Ok(monitor);
    }

    private static async Task<IResult> GetLoanAsync(
        Guid id,
        LoanService loanService,
        CancellationToken cancellationToken)
    {
        var loan = await loanService.GetDetailAsync(id, cancellationToken);

        return loan is null
            ? Results.NotFound()
            : Results.Ok(loan);
    }

    private static async Task<IResult> CreateLoanAsync(
        CreateLoanRequest request,
        LoanService loanService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var loan = await loanService.CreateAsync(request, userId, cancellationToken);

        return Results.Created($"/api/loans/{loan.LoanId}", loan);
    }

    private static async Task<IResult> SubmitForReviewAsync(
        Guid id,
        LoanService loanService,
        CancellationToken cancellationToken)
    {
        try
        {
            var loan = await loanService.SubmitForReviewAsync(id, cancellationToken);
            return loan is not null ? Results.Ok(loan) : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SubmitForApprovalAsync(
        Guid id,
        LoanService loanService,
        CancellationToken cancellationToken)
    {
        try
        {
            var loan = await loanService.SubmitForApprovalAsync(id, cancellationToken);
            return loan is not null ? Results.Ok(loan) : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ApproveLoanAsync(
        Guid id,
        ApproveLoanRequest request,
        LoanService loanService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        try
        {
            var loan = await loanService.ApproveAsync(
                id, userId, request.Remarks, cancellationToken);

            return loan is not null ? Results.Ok(loan) : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> RejectLoanAsync(
        Guid id,
        RejectLoanRequest request,
        LoanService loanService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        try
        {
            var loan = await loanService.RejectAsync(
                id, userId, request.RejectionReason, cancellationToken);

            return loan is not null ? Results.Ok(loan) : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public sealed record LoanQueryParams
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Search { get; init; }
    public LoanStatus? Status { get; init; }
    public string? BranchId { get; init; }
}
