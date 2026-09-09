using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public static class LoanEndpoints
{
    public static void MapLoanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapGet("/", async (
            [AsParameters] PaginationParams pagination,
            [FromQuery] bool? includeRelated,
            ILoanRepository loanRepository,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = user.GetUserId();
            var role = user.GetRole();
            var branchId = user.GetBranchId();

            var result = await loanRepository.GetAllAsync(
                pagination.Page,
                pagination.PageSize,
                role,
                branchId,
                userId,
                includeRelated ?? false,
                ct);

            return Results.Ok(ApiResponse<PagedResult<LoanApplication>>.SuccessResponse(result));
        })
        .WithName("ListLoans")
        .Produces<ApiResponse<PagedResult<LoanApplication>>>(200)
        .RequireAuthorization("CanViewLoan");

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
                LamId = loan.LamId,
                ApplicationGroupNo = loan.ApplicationGroupNo,
                BranchCode = loan.BranchCode,
                LoanNo = loan.LoanNo,
                ProductCode = loan.ProductCode,
                Product = loan.Product,
                CreationTypeCode = loan.CreationTypeCode,
                CreationTypeLabel = loan.CreationTypeLabel,
                RequestingOfficer = loan.RequestingOfficer,
                Lai = loan.Lai,
                CisId = loan.CisId,
                FirstName = loan.FirstName,
                MiddleName = loan.MiddleName,
                LastName = loan.LastName,
                Suffix = loan.Suffix,
                Birthdate = loan.Birthdate,
                Address = loan.Address,
                Agency = loan.Agency,
                Position = loan.Position,
                EmployeeId = loan.EmployeeId,
                NetTakeHomePay = loan.NetTakeHomePay,
                LengthOfService = loan.LengthOfService,
                Region = loan.Region,
                DivisionCode = loan.DivisionCode,
                StationCode = loan.StationCode,
                MisAgency = loan.MisAgency,
                School = loan.School,
                Referrer = loan.Referrer,
                Purpose = loan.Purpose,
                ProposedAmount = loan.ProposedAmount,
                TermDays = loan.TermDays,
                InterestRate = loan.InterestRate,
                NthpDate = loan.NthpDate,
                NotarialFee = loan.NotarialFee,
                DocStamps = loan.DocStamps,
                Insurance = loan.Insurance,
                StandardNotarialFee = loan.StandardNotarialFee,
                StandardDocStamps = loan.StandardDocStamps,
                StandardInsurance = loan.StandardInsurance,
                VerificationFindings = loan.VerificationFindings,
                HasDeviations = loan.HasDeviations,
                DeviationDetails = loan.DeviationDetails,
                DeviationJustifications = loan.DeviationJustifications,
                Remarks = loan.Remarks,
                AoRecommendation = loan.AoRecommendation,
                OtherRemarks = loan.OtherRemarks,
                FeeDeviationJustification = loan.FeeDeviationJustification,
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
                OutstandingLoans = loan.OutstandingLoans.Select(o => new OutstandingLoanResponse
                {
                    Id = o.Id,
                    Pn = o.Pn,
                    PrincipalBalance = o.PrincipalBalance,
                    Amortization = o.Amortization,
                    OutstandingBalance = o.OutstandingBalance,
                    DateGranted = o.DateGranted,
                    DateMaturity = o.DateMaturity,
                    Status = o.Status,
                    ProductWithDescription = o.ProductWithDescription,
                }).ToList(),
                BuyOuts = loan.BuyOuts.Select(b => new BuyOutResponse
                {
                    Id = b.Id,
                    Pn = b.Pn,
                    Name = b.Name,
                    Amortization = b.Amortization,
                    OutstandingBalance = b.OutstandingBalance,
                }).ToList(),
                EbiReloans = loan.EbiReloans.Select(e => new EbiReloanResponse
                {
                    Id = e.Id,
                    Pn = e.Pn,
                    Name = e.Name,
                    ExistingDeduction = e.ExistingDeduction,
                    OutstandingBalance = e.OutstandingBalance,
                    PayToClose = e.PayToClose,
                }).ToList(),
                IncomingLoans = loan.IncomingLoans.Select(i => new IncomingLoanResponse
                {
                    Id = i.Id,
                    Name = i.Name,
                    Deductions = i.Deductions,
                    Remarks = i.Remarks,
                }).ToList(),
                WebLoanCisNo = loan.WebLoanCisNo,
                WebLoanBranchCode = loan.WebLoanBranchCode,
                WebLoanAccountNumbers = loan.WebLoanAccountNumbers,
                WebLoanPnNumbers = loan.WebLoanPnNumbers,
                WebLoanLastSyncedAt = loan.WebLoanLastSyncedAt,
                PreLoanId = loan.PreLoanId,
                PreLoanFormNumber = loan.PreLoanFormNumber
            };

            return Results.Ok(ApiResponse<LoanResponse>.SuccessResponse(loanResponse));
        })
        .WithName("GetLoan")
        .Produces<ApiResponse<LoanResponse>>(200)
        .Produces<ApiResponse>(404)
        .RequireAuthorization("CanViewLoan");

        // ── GET /api/loans/{id}/history — chronological audit timeline ─────
        //
        // Reads LoanAction rows for one loan in ActionDate-ascending order.
        // The two-stage auth rule (permission OR ownership) is enforced
        // in-body — a single ASP.NET policy cannot express "loans.view OR
        // CreatedById == userId" without a custom requirement handler that
        // loads the loan, which is overkill for one endpoint. Group-level
        // .RequireAuthorization() at MapLoanEndpoints handles authentication;
        // this handler enforces the finer-grained rule.
        group.MapGet("/{id:int}/history", async (
            int id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();

            // 1 ── Existence check first (404 envelope matches the rest of the slice).
            var loan = await db.LoanApplications
                .AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new { l.Id, l.CreatedById })
                .FirstOrDefaultAsync(ct);

            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            // 2 ── Two-stage authorization.
            //    a) Anyone with `loans.view` (CanViewLoan) can read any loan's history.
            //    b) Otherwise the loan's creator may read their own history.
            var hasViewPerm = principal.HasPermission(Permissions.LoansView);
            var isCreator = loan.CreatedById == userId;
            if (!hasViewPerm && !isCreator)
                // Envelope-shaped 403 (parity with ForbiddenAccessException in the rest
                // of the slice). `Results.Forbid()` returns a bare 403 with no body,
                // which would break the FE's ApiResponse envelope parser.
                return Results.Json(
                    ApiResponse.ErrorResponse("You do not have permission to view this loan's history."),
                    statusCode: StatusCodes.Status403Forbidden);

            // 3 ── Project the timeline; cap at 500 per spec §3.3.
            //    ActionBy is resolved server-side through ActionByUser — never trust a
            //    client-supplied name.
            //    ThenBy(a => a.Id) gives stable ordering if two actions share a millisecond.
            //    Note: this handler reads AppDbContext directly rather than via
            //    ILoanRepository because ILoanRepository has no history method; a single
            //    EF projection here is cheaper than introducing a new repository method
            //    for one read path.
            var history = await db.LoanActions
                .AsNoTracking()
                .Where(a => a.LoanApplicationId == id)
                .OrderBy(a => a.ActionDate)
                .ThenBy(a => a.Id)
                .Take(500)
                .Select(a => new LoanHistoryEntryResponse(
                    a.Id,
                    $"{a.ActionByUser.FirstName} {a.ActionByUser.LastName}",
                    a.Action,
                    a.FromStatus,
                    a.ToStatus,
                    a.Comments,
                    a.ActionDate))
                .ToListAsync(ct);

            return Results.Ok(ApiResponse<List<LoanHistoryEntryResponse>>.SuccessResponse(history));
        })
        .WithName("GetLoanHistory")
        .Produces<ApiResponse<List<LoanHistoryEntryResponse>>>(200)
        .Produces<ApiResponse>(404)
        .Produces<ApiResponse>(403);

        // ── POST /api/loans — multi-loan submission ───────────────────────
        //
        // Trust boundary: officer name + branch code are derived from the JWT /
        // Users table. The request body's branchType.requestingOfficer and
        // loans[].branchCode are echoed back for shape-compat only; the server
        // overwrites them. An Idempotency-Key header (GUID) is MANDATORY: a
        // double-click / axios 401-replay / React Query retry must never mint
        // a second group of LAM IDs.
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
                LamId = loan.LamId,
                ApplicationGroupNo = loan.ApplicationGroupNo,
                BranchCode = loan.BranchCode,
                LoanNo = loan.LoanNo,
                ProductCode = loan.ProductCode,
                Product = loan.Product,
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
                Purpose = loan.Purpose,
                ProposedAmount = loan.ProposedAmount,
                TermDays = loan.TermDays,
                InterestRate = loan.InterestRate,
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

public class LoanResponse
{
    public int Id { get; set; }
    public string LamId { get; set; } = string.Empty;
    public string ApplicationGroupNo { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string LoanNo { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;

    public int? CreationTypeCode { get; set; }
    public string? CreationTypeLabel { get; set; }
    public string? RequestingOfficer { get; set; }
    public string? Lai { get; set; }

    public string? CisId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? Suffix { get; set; }
    public DateOnly? Birthdate { get; set; }
    public string? Address { get; set; }
    public string? Agency { get; set; }
    public string? Position { get; set; }
    public string? EmployeeId { get; set; }
    public decimal? NetTakeHomePay { get; set; }
    public string? LengthOfService { get; set; }
    public string? Region { get; set; }
    public string? DivisionCode { get; set; }
    public string? StationCode { get; set; }
    public string? MisAgency { get; set; }
    public string? School { get; set; }
    public string? Referrer { get; set; }

    public string? Purpose { get; set; }
    public decimal ProposedAmount { get; set; }
    public int TermDays { get; set; }
    public decimal InterestRate { get; set; }
    public DateOnly? NthpDate { get; set; }

    public decimal NotarialFee { get; set; }
    public decimal DocStamps { get; set; }
    public decimal Insurance { get; set; }
    public decimal StandardNotarialFee { get; set; }
    public decimal StandardDocStamps { get; set; }
    public decimal StandardInsurance { get; set; }

    public string? VerificationFindings { get; set; }
    public bool HasDeviations { get; set; }
    public List<string> DeviationDetails { get; set; } = new();
    public Dictionary<string, string> DeviationJustifications { get; set; } = new();
    public string? Remarks { get; set; }
    public string? AoRecommendation { get; set; }
    public string? OtherRemarks { get; set; }
    public string? FeeDeviationJustification { get; set; }

    public string Status { get; set; } = string.Empty;
    public DateTime ApplicationDate { get; set; }
    public DateTime LastActionDate { get; set; }
    public int CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public List<LoanActionResponse> Actions { get; set; } = new();

    public List<OutstandingLoanResponse> OutstandingLoans { get; set; } = new();
    public List<BuyOutResponse> BuyOuts { get; set; } = new();
    public List<EbiReloanResponse> EbiReloans { get; set; } = new();
    public List<IncomingLoanResponse> IncomingLoans { get; set; } = new();

    // WebLoan Traceability
    public string? WebLoanCisNo { get; set; }
    public string? WebLoanBranchCode { get; set; }
    public List<string> WebLoanAccountNumbers { get; set; } = new();
    public List<string> WebLoanPnNumbers { get; set; } = new();
    public DateTime? WebLoanLastSyncedAt { get; set; }
    public int? PreLoanId { get; set; }
    public string? PreLoanFormNumber { get; set; }
}

public class OutstandingLoanResponse
{
    public int Id { get; set; }
    public string Pn { get; set; } = string.Empty;
    public decimal PrincipalBalance { get; set; }
    public decimal Amortization { get; set; }
    public decimal OutstandingBalance { get; set; }
    public DateOnly? DateGranted { get; set; }
    public DateOnly? DateMaturity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProductWithDescription { get; set; }
}

public class BuyOutResponse
{
    public int Id { get; set; }
    public string Pn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amortization { get; set; }
    public decimal OutstandingBalance { get; set; }
}

public class EbiReloanResponse
{
    public int Id { get; set; }
    public string Pn { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal ExistingDeduction { get; set; }
    public decimal OutstandingBalance { get; set; }
    public decimal PayToClose { get; set; }
}

public class IncomingLoanResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Deductions { get; set; }
    public string Remarks { get; set; } = string.Empty;
}

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

public class UpdateLoanStatusRequest
{
    public string Status { get; init; } = string.Empty;
    public string? Comments { get; init; }
}

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