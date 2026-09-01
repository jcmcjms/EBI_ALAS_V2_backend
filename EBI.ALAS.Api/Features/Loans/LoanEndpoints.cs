using System.Security.Claims;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Loan endpoint definitions using Minimal APIs.
/// </summary>

public static class LoanEndpoints
{
    public static void MapLoanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        // GET /api/loans
        group.MapGet("/", async (
            [AsParameters] PaginationParams pagination,
            ILoanRepository loanRepository,
            ClaimsPrincipal user) =>
        {
            var userId = user.GetUserId();
            var role = user.GetRole();
            var branchId = user.GetBranchId();

            var result = await loanRepository.GetAllAsync(
                pagination.Page,
                pagination.PageSize,
                role,
                branchId,
                userId);

            return Results.Ok(ApiResponse<PagedResult<LoanApplication>>.SuccessResponse(result));
        })
        .WithName("ListLoans")
        .Produces<ApiResponse<PagedResult<LoanApplication>>>(200)
        .RequireAuthorization("CanViewLoan");

        // GET /api/loans/{id}
        group.MapGet("/{id:int}", async (
            int id,
            ILoanRepository loanRepository,
            IAuditLogger auditLogger) =>
        {
            var loan = await loanRepository.GetByIdAsync(id, includeRelated: true);
            if (loan == null)
            {
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            }

            var loanResponse = new LoanResponse
            {
                Id = loan.Id,
                FormNumber = loan.FormNumber,
                BranchCode = loan.BranchCode,
                CisId = loan.CisId,
                FirstName = loan.FirstName,
                MiddleName = loan.MiddleName,
                LastName = loan.LastName,
                Agency = loan.Agency,
                Position = loan.Position,
                EmployeeId = loan.EmployeeId,
                NetTakeHomePay = loan.NetTakeHomePay,
                School = loan.School,
                Referrer = loan.Referrer,
                Product = loan.Product,
                Purpose = loan.Purpose,
                ProposedAmount = loan.ProposedAmount,
                TermMonths = loan.TermMonths,
                InterestRate = loan.InterestRate,
                ModeOfPayment = loan.ModeOfPayment,
                DateOfFirstRelease = loan.DateOfFirstRelease,
                CoMaker = loan.CoMaker,
                Status = loan.Status,
                ApplicationDate = loan.ApplicationDate,
                LastActionDate = loan.LastActionDate,
                CreatedById = loan.CreatedById,
                CreatedByName = $"{loan.CreatedBy.FirstName} {loan.CreatedBy.LastName}",
                Actions = loan.Actions.Select(a => new LoanActionResponse
                {
                    Id = a.Id,
                    Action = a.Action,
                    FromStatus = a.FromStatus,
                    ToStatus = a.ToStatus,
                    Comments = a.Comments,
                    ActionDate = a.ActionDate,
                    ActionByUserName = $"{a.ActionByUser.FirstName} {a.ActionByUser.LastName}"
                }).ToList(),

                // WebLoan Traceability
                WebLoanCisNo = loan.WebLoanCisNo,
                WebLoanBranchCode = loan.WebLoanBranchCode,
                WebLoanAccountNumbers = loan.WebLoanAccountNumbers,
                WebLoanPnNumbers = loan.WebLoanPnNumbers,
                WebLoanLastSyncedAt = loan.WebLoanLastSyncedAt
            };

            return Results.Ok(ApiResponse<LoanResponse>.SuccessResponse(loanResponse));
        })
        .WithName("GetLoan")
        .Produces<ApiResponse<LoanResponse>>(200)
        .Produces<ApiResponse>(404)
        .RequireAuthorization("CanViewLoan");

        // POST /api/loans
        group.MapPost("/", async (
            [FromBody] CreateLoanRequest request,
            IValidator<CreateLoanRequest> validator,
            ILoanRepository loanRepository,
            IFormNumberGenerator formNumberGenerator,
            IAuditLogger auditLogger,
            ITimeProvider timeProvider,
            ClaimsPrincipal user) =>
        {
            // Validate request
            var validationResult = await validator.ValidateAsync(request);
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

            var userId = user.GetUserId();
            var formNumber = await formNumberGenerator.GenerateFormNumberAsync();

            var loan = new LoanApplication
            {
                FormNumber = formNumber,
                BranchCode = request.BranchCode,
                CisId = request.CisId,
                FirstName = request.FirstName,
                MiddleName = request.MiddleName,
                LastName = request.LastName,
                Agency = request.Agency,
                Position = request.Position,
                EmployeeId = request.EmployeeId,
                NetTakeHomePay = request.NetTakeHomePay,
                School = request.School,
                Referrer = request.Referrer,
                Product = request.Product,
                Purpose = request.Purpose,
                ProposedAmount = request.ProposedAmount,
                TermMonths = request.TermMonths,
                InterestRate = request.InterestRate,
                ModeOfPayment = request.ModeOfPayment,
                DateOfFirstRelease = request.DateOfFirstRelease,
                CoMaker = request.CoMaker,
                Status = "Draft",
                ApplicationDate = timeProvider.UtcNow,
                LastActionDate = timeProvider.UtcNow,
                CreatedById = userId
            };

            var createdLoan = await loanRepository.CreateAsync(loan);

            // Log the creation action
            await auditLogger.LogActionAsync(
                createdLoan.Id,
                userId,
                "Created",
                null,
                "Draft",
                "Loan application created");

            var response = new LoanResponse
            {
                Id = createdLoan.Id,
                FormNumber = createdLoan.FormNumber,
                BranchCode = createdLoan.BranchCode,
                CisId = createdLoan.CisId,
                FirstName = createdLoan.FirstName,
                MiddleName = createdLoan.MiddleName,
                LastName = createdLoan.LastName,
                Agency = createdLoan.Agency,
                Position = createdLoan.Position,
                EmployeeId = createdLoan.EmployeeId,
                NetTakeHomePay = createdLoan.NetTakeHomePay,
                School = createdLoan.School,
                Referrer = createdLoan.Referrer,
                Product = createdLoan.Product,
                Purpose = createdLoan.Purpose,
                ProposedAmount = createdLoan.ProposedAmount,
                TermMonths = createdLoan.TermMonths,
                InterestRate = createdLoan.InterestRate,
                ModeOfPayment = createdLoan.ModeOfPayment,
                DateOfFirstRelease = createdLoan.DateOfFirstRelease,
                CoMaker = createdLoan.CoMaker,
                Status = createdLoan.Status,
                ApplicationDate = createdLoan.ApplicationDate,
                LastActionDate = createdLoan.LastActionDate,
                CreatedById = createdLoan.CreatedById,
                CreatedByName = user.GetFirstName() + " " + user.GetLastName()
            };

            return Results.Created($"/api/loans/{createdLoan.Id}",
                ApiResponse<LoanResponse>.SuccessResponse(response, "Loan created successfully"));
        })
        .WithName("CreateLoan")
        .Produces<ApiResponse<LoanResponse>>(201)
        .Produces<ApiResponse>(400)
        .RequireAuthorization("CanCreateLoan");

        // PUT /api/loans/{id}/status
        group.MapPut("/{id:int}/status", async (
            int id,
            [FromBody] UpdateLoanStatusRequest request,
            IValidator<UpdateLoanStatusRequest> validator,
            ILoanRepository loanRepository,
            ILoanWorkflowService workflowService,
            IAuditLogger auditLogger,
            ClaimsPrincipal user,
            ITimeProvider timeProvider) =>
        {
            // Validate request
            var validationResult = await validator.ValidateAsync(request);
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

            var loan = await loanRepository.GetByIdAsync(id);
            if (loan == null)
            {
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            }

            var userRole = user.GetRole();
            var userId = user.GetUserId();

            // Validate workflow transition
            if (!workflowService.IsValidTransition(loan.Status, request.Status, userRole))
            {
                return Results.BadRequest(ApiResponse.ErrorResponse(
                    $"Invalid status transition from {loan.Status} to {request.Status} for role {userRole}"));
            }

            var fromStatus = loan.Status;
            loan.Status = request.Status;
            loan.LastActionDate = timeProvider.UtcNow;

            await loanRepository.UpdateAsync(loan);

            // Log the status change
            await auditLogger.LogActionAsync(
                id,
                userId,
                "StatusChanged",
                fromStatus,
                request.Status,
                request.Comments);

            var response = new LoanResponse
            {
                Id = loan.Id,
                FormNumber = loan.FormNumber,
                BranchCode = loan.BranchCode,
                CisId = loan.CisId,
                FirstName = loan.FirstName,
                MiddleName = loan.MiddleName,
                LastName = loan.LastName,
                Agency = loan.Agency,
                Position = loan.Position,
                EmployeeId = loan.EmployeeId,
                NetTakeHomePay = loan.NetTakeHomePay,
                School = loan.School,
                Referrer = loan.Referrer,
                Product = loan.Product,
                Purpose = loan.Purpose,
                ProposedAmount = loan.ProposedAmount,
                TermMonths = loan.TermMonths,
                InterestRate = loan.InterestRate,
                ModeOfPayment = loan.ModeOfPayment,
                DateOfFirstRelease = loan.DateOfFirstRelease,
                CoMaker = loan.CoMaker,
                Status = loan.Status,
                ApplicationDate = loan.ApplicationDate,
                LastActionDate = loan.LastActionDate,
                CreatedById = loan.CreatedById,
                CreatedByName = user.GetFirstName() + " " + user.GetLastName()
            };

            return Results.Ok(ApiResponse<LoanResponse>.SuccessResponse(response, "Loan status updated successfully"));
        })
        .WithName("UpdateLoanStatus")
        .Produces<ApiResponse<LoanResponse>>(200)
        .Produces<ApiResponse>(400)
        .Produces<ApiResponse>(404);
    }
}

/// <summary>
/// Loan response DTO.
/// </summary>
public class LoanResponse
{
    public int Id { get; set; }
    public string FormNumber { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string? CisId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? Agency { get; set; }
    public string? Position { get; set; }
    public string? EmployeeId { get; set; }
    public decimal? NetTakeHomePay { get; set; }
    public string? School { get; set; }
    public string? Referrer { get; set; }
    public string Product { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public decimal ProposedAmount { get; set; }
    public int TermMonths { get; set; }
    public decimal InterestRate { get; set; }
    public string? ModeOfPayment { get; set; }
    public DateTime? DateOfFirstRelease { get; set; }
    public string? CoMaker { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ApplicationDate { get; set; }
    public DateTime LastActionDate { get; set; }
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public List<LoanActionResponse> Actions { get; set; } = new();

    // WebLoan Traceability
    public string? WebLoanCisNo { get; set; }
    public string? WebLoanBranchCode { get; set; }
    public List<string> WebLoanAccountNumbers { get; set; } = new();
    public List<string> WebLoanPnNumbers { get; set; } = new();
    public DateTime? WebLoanLastSyncedAt { get; set; }
}

/// <summary>
/// Loan action response DTO.
/// </summary>
public class LoanActionResponse
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Comments { get; set; }
    public DateTime ActionDate { get; set; }
    public string ActionByUserName { get; set; } = string.Empty;
}

/// <summary>
/// Create loan request DTO.
/// </summary>
public class CreateLoanRequest
{
    public string BranchCode { get; init; } = string.Empty;
    public string? CisId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string? Agency { get; init; }
    public string? Position { get; init; }
    public string? EmployeeId { get; init; }
    public decimal? NetTakeHomePay { get; init; }
    public string? School { get; init; }
    public string? Referrer { get; init; }
    public string Product { get; init; } = string.Empty;
    public string? Purpose { get; init; }
    public decimal ProposedAmount { get; init; }
    public int TermMonths { get; init; }
    public decimal InterestRate { get; init; }
    public string? ModeOfPayment { get; init; }
    public DateTime? DateOfFirstRelease { get; init; }
    public string? CoMaker { get; init; }
}

/// <summary>
/// Update loan status request DTO.
/// </summary>
public class UpdateLoanStatusRequest
{
    public string Status { get; init; } = string.Empty;
    public string? Comments { get; init; }
}

/// <summary>
/// FluentValidation validator for CreateLoanRequest.
/// </summary>
public class CreateLoanValidator : AbstractValidator<CreateLoanRequest>
{
    public CreateLoanValidator()
    {
        RuleFor(x => x.BranchCode)
            .NotEmpty()
            .WithMessage("Branch code is required")
            .MaximumLength(20)
            .WithMessage("Branch code must not exceed 20 characters");

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .WithMessage("First name is required")
            .MaximumLength(100)
            .WithMessage("First name must not exceed 100 characters");

        RuleFor(x => x.LastName)
            .NotEmpty()
            .WithMessage("Last name is required")
            .MaximumLength(100)
            .WithMessage("Last name must not exceed 100 characters");

        RuleFor(x => x.Product)
            .NotEmpty()
            .WithMessage("Product is required")
            .MaximumLength(100)
            .WithMessage("Product must not exceed 100 characters");

        RuleFor(x => x.ProposedAmount)
            .GreaterThan(0)
            .WithMessage("Proposed amount must be greater than 0");

        RuleFor(x => x.TermMonths)
            .GreaterThan(0)
            .WithMessage("Term months must be greater than 0");

        RuleFor(x => x.InterestRate)
            .InclusiveBetween(0, 100)
            .WithMessage("Interest rate must be between 0 and 100");

        // Manual-entry fields — first-line defense against payload bloat.
        // Mirrors the Zod schema on the frontend so the two never drift.
        RuleFor(x => x.School)
            .MaximumLength(200)
            .WithMessage("School name must not exceed 200 characters");

        RuleFor(x => x.Referrer)
            .MaximumLength(100)
            .WithMessage("Referrer name must not exceed 100 characters");
    }
}

/// <summary>
/// FluentValidation validator for UpdateLoanStatusRequest.
/// </summary>
public class UpdateLoanStatusValidator : AbstractValidator<UpdateLoanStatusRequest>
{
    private static readonly string[] ValidStatuses = new[]
    {
        "Draft", "ForRecommendation", "ForChecking", "ForApproval",
        "Approved", "Rejected", "ForRevision", "ForDisbursement",
        "Disbursed", "OnGoing"
    };

    public UpdateLoanStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .WithMessage("Status is required")
            .Must(status => ValidStatuses.Contains(status))
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}");

        RuleFor(x => x.Comments)
            .MaximumLength(1000)
            .WithMessage("Comments must not exceed 1000 characters");
    }
}
