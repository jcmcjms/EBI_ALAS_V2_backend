using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Notifications;
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

        // ── GET /api/loans — paginated list of loan submissions ───────────
        //
        // Returns ApiResponse<PagedResult<LoanSubmissionResponse>> — the
        // SAME response envelope POST /api/loans returns. The frontend can
        // deserialize both list and create responses with the same
        // `LoanSubmissionResponse` type (one DTO to maintain, no drift).
        //
        // Query parameters (names align with POST's SubmitLoanApplicationRequest
        // sections so the two surfaces stay in lock-step):
        //   • page / pageSize  — pagination (pageSize capped 1..100)
        //   • search           — matches ApplicationGroupNo, FirstName, or LastName
        //   • status           — comma-separated workflow statuses (e.g. "Draft,ForRecommendation")
        //   • branchCode       — admin-only filter; non-admins are auto-scoped to their branch
        //   • fromDate / toDate — application-date range filter (inclusive end-of-day on toDate)
        //   • sortBy / sortDesc — whitelisted columns (applicationdate, proposedamount,
        //                         status, customername); unknown values fall back to
        //                         ApplicationDate DESC
        group.MapGet("/", async (
            HttpContext ctx,
            AppDbContext db,
            int? page,
            int? pageSize,
            string? search,
            string? status,
            string? branchCode,
            string? sortBy,
            bool? sortDesc,
            DateTime? fromDate,
            DateTime? toDate,
            CancellationToken ct) =>
        {
            // ── Pagination caps ────────────────────────────────────────
            var p = Math.Max(page ?? 1, 1);
            var ps = Math.Clamp(pageSize ?? 15, 1, 100);

            // ── Base query (raw entity) ─────────────────────────────────
            // Include CreatedBy so the projection's `l.CreatedBy.FirstName +
            // l.CreatedBy.LastName` resolves via a single LEFT JOIN instead of
            // triggering an N+1 round-trip per page row.
            //
            // Typed as `IQueryable<LoanApplication>` (not var) because
            // `.Include(...)` returns `IIncludableQueryable<T, P>` and
            // subsequent `.Where()` / `.OrderBy()` calls return plain
            // `IQueryable<T>` — without an explicit type the compiler
            // can't reconcile the two and emits CS0266 on every chain.
            // EF Core still emits the JOIN internally; the static type
            // is just narrower for the benefit of subsequent assignments.
            IQueryable<LoanApplication> query = db.LoanApplications
                .AsNoTracking()
                .Include(l => l.CreatedBy);

            // ── Branch scoping (anti-enumeration) ──────────────────────
            var userRole = ctx.User.GetRole();
            var userBranchCode = ctx.User.GetBranchCode();

            if (!string.IsNullOrEmpty(userBranchCode)
                && !string.Equals(userRole, Roles.Admin, StringComparison.Ordinal))
            {
                query = query.Where(l => l.BranchCode == userBranchCode);
            }

            // ── Search ─────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(l =>
                    l.ApplicationGroupNo.Contains(s) ||
                    l.FirstName.Contains(s) ||
                    l.LastName.Contains(s));
            }

            // ── Status (comma-separated multi-select) ─────────────────
            if (!string.IsNullOrWhiteSpace(status))
            {
                var statuses = status
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
                if (statuses.Length > 0)
                {
                    query = query.Where(l => statuses.Contains(l.Status));
                }
            }

            // ── Branch filter (admin only) ────────────────────────────
            if (string.Equals(userRole, Roles.Admin, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(branchCode)
                && !string.Equals(branchCode, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(l => l.BranchCode == branchCode);
            }

            // ── Application-date range ─────────────────────────────────
            // Inclusive on both ends. `toDate` is treated as end-of-day
            // so a user picking the same calendar date for from and to
            // still gets every loan filed that day (the <input type="date">
            // picker on the FE submits midnight on each end).
            if (fromDate.HasValue)
            {
                var startDate = fromDate.Value.Date;
                query = query.Where(l => l.ApplicationDate >= startDate);
            }
            if (toDate.HasValue)
            {
                var endDate = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(l => l.ApplicationDate <= endDate);
            }

            // ── Sorting (whitelist + switch) ────────────────────────────
            // Sorting MUST happen on the raw entity — EF can translate
            // ORDER BY on entity columns but NOT on computed DTO properties.
            query = sortBy?.ToLower() switch
            {
                "applicationdate" => sortDesc == true
                    ? query.OrderByDescending(l => l.ApplicationDate)
                    : query.OrderBy(l => l.ApplicationDate),
                "proposedamount" => sortDesc == true
                    ? query.OrderByDescending(l => l.ProposedAmount)
                    : query.OrderBy(l => l.ProposedAmount),
                "status" => sortDesc == true
                    ? query.OrderByDescending(l => l.Status)
                    : query.OrderBy(l => l.Status),
                "customername" => sortDesc == true
                    ? query.OrderByDescending(l => l.LastName)
                    : query.OrderBy(l => l.LastName),
                _ => query.OrderByDescending(l => l.ApplicationDate)
            };

            // ── Project to LoanSubmissionResponse (POST-compatible) ─────
            //
            // Group by ApplicationGroupNo so each page item mirrors one
            // POST response (one ApplicationGroupNo + its N Loans). The
            // .Select() is composed to project the entity → CreatedLoan
            // shape in a single SQL statement; the group-by happens in
            // memory because EF cannot translate GroupBy → Dictionary
            // grouping without an aggregate that would change the result.
            //
            // We materialize the page first (rows), then group in-memory.
            // For typical banking volumes (15..100 rows/page) this is
            // cheaper than a second DB round-trip.
            var totalCount = await query.CountAsync(ct);

            var rows = await query
                .Select(l => new
                {
                    l.ApplicationGroupNo,
                    l.Id,
                    l.LamId,
                    l.LoanNo,
                    l.ProductCode,
                    l.Product,
                    l.ProposedAmount,
                    l.Status,
                    l.BranchCode,
                    l.CreationTypeCode,
                    l.CreationTypeLabel,
                    l.FirstName,
                    l.MiddleName,
                    l.LastName,
                    l.Suffix,
                    // ── Monitoring-table enrichment (NEW) ───────────────
                    l.ApplicationDate,
                    l.LastActionDate,
                    CreatedByName = l.CreatedBy.FirstName + " " + l.CreatedBy.LastName,
                })
                .Skip((p - 1) * ps)
                .Take(ps)
                .ToListAsync(ct);

            var submissions = rows
                .GroupBy(r => r.ApplicationGroupNo)
                .Select(g => new LoanSubmissionResponse
                {
                    ApplicationGroupNo = g.Key,
                    Loans = g
                        .Select(r => new CreatedLoan
                        {
                            Id = r.Id,
                            LamId = r.LamId,
                            LoanNo = r.LoanNo,
                            ProductCode = r.ProductCode,
                            Product = r.Product,
                            ProposedAmount = r.ProposedAmount,
                            Status = r.Status,
                            BranchCode = r.BranchCode,
                            CreationTypeCode = r.CreationTypeCode,
                            CreationTypeLabel = r.CreationTypeLabel,
                            FirstName = r.FirstName,
                            MiddleName = r.MiddleName,
                            LastName = r.LastName,
                            Suffix = r.Suffix,
                            // ── Monitoring-table enrichment (NEW) ───────────
                            ApplicationDate = r.ApplicationDate,
                            LastActionDate = r.LastActionDate,
                            CreatedByName = r.CreatedByName,
                        })
                        .ToList(),
                })
                .ToList();

            var pagedResult = new PagedResult<LoanSubmissionResponse>(
                submissions, totalCount, p, ps);

            return Results.Ok(ApiResponse<PagedResult<LoanSubmissionResponse>>.SuccessResponse(
                pagedResult, "Loans retrieved successfully"));
        })
        .WithName("GetLoans")
        .Produces<ApiResponse<PagedResult<LoanSubmissionResponse>>>(200)
        .Produces<ApiResponse>(401)
        .Produces<ApiResponse>(403)
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
            INotificationService notificationService,
            ClaimsPrincipal user,
            ITimeProvider timeProvider,
            CancellationToken ct) =>
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

            // ── Notifications ──────────────────────────────────────────
            // Two-tier routing:
            //   1. The "next role in the workflow chain" (Evaluator /
            //      Approver / Encoder) gets a specific, action-oriented
            //      message so the bell is meaningful.
            //   2. The original Encoder gets a generic status update so
            //      they can see their submission moving — UNLESS they
            //      are the actor, in which case we skip the self-ping.
            var link = $"/loans/monitoring?id={id}";
            var actorName = $"{user.GetFirstName()} {user.GetLastName()}";
            var clientName = $"{loan.FirstName} {loan.LastName}";

            if (request.Status == "ForChecking")
            {
                var evaluators = await loanRepository.GetUsersByRoleAndBranchAsync(
                    Roles.Evaluator, loan.BranchCode, ct);
                foreach (var e in evaluators)
                {
                    await notificationService.CreateAsync(
                        e.Id,
                        "Ready for Evaluation",
                        $"{actorName} recommended {clientName}'s application ({loan.LamId}).",
                        link);
                }
            }
            else if (request.Status == "ForApproval")
            {
                var approvers = await loanRepository.GetUsersByRoleAndBranchAsync(
                    Roles.Approver, loan.BranchCode, ct);
                foreach (var a in approvers)
                {
                    await notificationService.CreateAsync(
                        a.Id,
                        "Ready for Approval",
                        $"{actorName} completed evaluation for {clientName}'s application ({loan.LamId}).",
                        link);
                }
            }
            else if (request.Status == "ForRevision")
            {
                // Pushback: notify the original Encoder with the reviewer's comments.
                var pushbackRole = userRole == Roles.Recommender ? "Branch Head"
                                 : userRole == Roles.Approver ? "Area Head"
                                 : "Reviewer";
                
                await notificationService.CreateAsync(
                    loan.CreatedById,
                    "Application Returned for Revision",
                    $"{pushbackRole} {actorName} returned {clientName}'s application ({loan.LamId}). Reason: {request.Comments}",
                    link);
            }

            // "Update auth user application status" — always notify the
            // original Encoder (if it isn't the actor themselves) so they
            // have a timeline of their submission's progress. Skips the
            // noisy self-ping when the encoder moves their own draft.
            if (loan.CreatedById != userId)
            {
                await notificationService.CreateAsync(
                    loan.CreatedById,
                    $"Status Update: {request.Status}",
                    $"Your application for {clientName} ({loan.LamId}) has been updated to {request.Status}.",
                    link);
            }

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

    /// <summary>
    /// Statuses that require mandatory comments for audit compliance.
    /// Pushbacks and rejections must explain WHY; advances should document conditions.
    /// </summary>
    private static readonly string[] RequireComments = new[]
    {
        "ForRevision",  // Pushback (Recommender → Encoder, or Approver → Encoder)
        "Rejected"      // Rejection (Approver only)
    };

    public UpdateLoanStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .WithMessage("Status is required")
            .Must(status => ValidStatuses.Contains(status))
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}");

        // Comments are MANDATORY for pushbacks and rejections (audit trail).
        When(x => RequireComments.Contains(x.Status), () =>
        {
            RuleFor(x => x.Comments)
                .NotEmpty()
                .WithMessage("Comments are required when pushing back or rejecting an application.")
                .MinimumLength(10)
                .WithMessage("Please provide a detailed explanation (at least 10 characters).");
        });

        // Max length applies to all comments regardless of whether required.
        RuleFor(x => x.Comments)
            .MaximumLength(2000)
            .WithMessage("Comments must not exceed 2000 characters");
    }
}