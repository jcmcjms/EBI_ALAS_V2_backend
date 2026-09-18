using System.Security.Claims;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Loans.Endpoints;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public static class LoanEndpoints
{
    public static void MapLoanEndpoints(this WebApplication app)
    {
        app.MapGetLoansEndpoints();
        app.MapGetLoanByIdEndpoints();
        app.MapGetLoanHistoryEndpoints();
        app.MapGetSlaPolicyEndpoints();
        app.MapGetQueueDefaultEndpoints();
        app.MapCreateLoanEndpoints();
        app.MapUpdateLoanStatusEndpoints();
        app.MapCancelLoanEndpoints();

        // ── POST /api/loans/{id}/assignment/release — release an active lease ──
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapPost("/{id:int}/assignment/release", async (
            int id,
            ClaimsPrincipal principal,
            ILoanAssignmentService assignment,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            await assignment.ReleaseAsync(id, userId, ct);
            return Results.Ok(ApiResponse.SuccessResponse("Assignment released."));
        })
        .WithName("ReleaseAssignment")
        .Produces<ApiResponse>(200)
        .RequireAuthorization("CanViewLoan");

        // ── GET /api/loans/{id}/routing — tier + matched rule + completeness ──
        group.MapGet("/{id:int}/routing", async (
            int id,
            AppDbContext db,
            IDocumentCompletenessService completeness,
            CancellationToken ct) =>
        {
            var loan = await db.LoanApplications
                .AsNoTracking()
                .Include(l => l.Deviations)
                .FirstOrDefaultAsync(l => l.Id == id, ct);

            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            var completenessResult = await completeness.CheckAsync(loan, ct);

            // Resolve assigned approver name
            string? assignedName = null;
            if (loan.AssignedApproverId is { } assigneeId)
            {
                var assignee = await db.Users.AsNoTracking()
                    .Where(u => u.Id == assigneeId)
                    .Select(u => new { u.FirstName, u.LastName })
                    .FirstOrDefaultAsync(ct);
                if (assignee is not null)
                    assignedName = $"{assignee.FirstName} {assignee.LastName}";
            }

            // Resolve matched rule description
            string? matchedRule = null;
            if (loan.RequiredApprovalTier is { } tier)
            {
                var auth = await db.ApprovalAuthorities.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Tier == tier, ct);
                if (auth is not null)
                    matchedRule = $"{auth.DisplayName} (Tier {tier})";
            }

            var dto = new LoanRoutingDto
            {
                LoanId = loan.Id,
                RequiredApprovalTier = loan.RequiredApprovalTier,
                DeviationSeverity = (int)loan.DeviationSeverity,
                TotalExposure = loan.TotalExposure,
                LoanType = loan.LoanType,
                MatchedRule = matchedRule,
                DocumentsComplete = completenessResult.Complete,
                MissingDocuments = completenessResult.Missing.ToList(),
                AssignedApproverId = loan.AssignedApproverId,
                AssignedApproverName = assignedName,
            };

            return Results.Ok(ApiResponse<LoanRoutingDto>.SuccessResponse(dto));
        })
        .WithName("GetLoanRouting")
        .Produces<ApiResponse<LoanRoutingDto>>(200)
        .Produces<ApiResponse>(404)
        .RequireAuthorization("CanViewLoan");

        // ── POST /api/loans/{id}/documents/verify — on-demand completeness recheck ──
        group.MapPost("/{id:int}/documents/verify", async (
            int id,
            AppDbContext db,
            IDocumentCompletenessService completeness,
            ITimeProvider time,
            CancellationToken ct) =>
        {
            var loan = await db.LoanApplications.FindAsync([id], ct);
            if (loan is null) return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            var result = await completeness.CheckByLoanNoAsync(loan.LoanNo, ct, bypassCache: true);
            loan.DocumentsCompleteAt = result.Complete ? time.UtcNow : null;
            await db.SaveChangesAsync(ct);

            return Results.Ok(ApiResponse<object>.SuccessResponse(new
            {
                complete = result.Complete,
                missing = result.Missing,
                documentsCompleteAt = loan.DocumentsCompleteAt,
            }, result.Complete
                ? "All required documents are uploaded."
                : $"{result.Missing.Count} document(s) still missing."));
        })
        .WithName("VerifyLoanDocuments")
        .Produces<ApiResponse<object>>(200)
        .Produces<ApiResponse>(404)
        .RequireAuthorization("CanViewLoan");
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

    // ── Approval form convention fields (frozen at submission) ──────
    /// <summary>webloan loan_data.total_amortization: amortization period count (e.g. 84).</summary>
    public int? PolicyTermMonths { get; set; }

    /// <summary>Frozen TERM (Days) printed on the approval form at submission time.</summary>
    public int? ApprovalTermDays { get; set; }

    /// <summary>Frozen annual rate in percent (e.g. 21.57) normalized at submission time.</summary>
    public decimal? AnnualRatePercent { get; set; }

    public decimal NotarialFee { get; set; }
    public decimal DocStamps { get; set; }
    public decimal Insurance { get; set; }
    public decimal StandardNotarialFee { get; set; }
    public decimal StandardDocStamps { get; set; }
    public decimal StandardInsurance { get; set; }
    public decimal StandardApplicationCharge { get; set; }
    public decimal StandardAdvanceInterest { get; set; }

    // ── Computed snapshot (server-authoritative) ───────────────────────
    public decimal TotalDeductions { get; set; }
    public decimal DeductionRate { get; set; }
    public decimal GrossProceeds { get; set; }
    public decimal NetProceedsOnDS { get; set; }
    public decimal NetProceedsToClient { get; set; }
    public decimal TotalExposure { get; set; }
    public decimal? MonthlyAmortization { get; set; }
    public decimal NetPayAfterDeduction { get; set; }
    public decimal GrossDisposableIncome { get; set; }
    public decimal CapacityDeductions { get; set; }
    public decimal NetDisposableIncome { get; set; }
    public decimal MaximumLoanableAmount { get; set; }
    public bool AmortizationExceedsDisposable { get; set; }
    public bool NthpBelowMinimum { get; set; }

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

    /// <summary>Officer the application last flowed through (resolved from
    /// the latest LoanAction). Falls back to creator when no action exists.</summary>
    public string? LastActionByName { get; set; }

    /// <summary>Verb of the latest workflow action (Created, StatusChanged,
    /// PushedBack, EvaluatedRecommended, EvaluatedNotRecommended…).</summary>
    public string? LastAction { get; set; }

    /// <summary>Latest evaluation verdict projected from the audit trail.
    /// Values: "EvaluatedRecommended" | "EvaluatedNotRecommended" | null.</summary>
    public string? EvaluationVerdict { get; set; }

    // ── Delegation-of-authority routing ─────────────────────────────
    public string LoanType { get; set; } = "New";
    public int DeviationSeverity { get; set; }
    public int? RequiredApprovalTier { get; set; }
    public int? AssignedApproverId { get; set; }
    public string? AssignedApproverName { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? DocumentsCompleteAt { get; set; }

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

public class LoanRoutingDto
{
    public int LoanId { get; set; }
    public int? RequiredApprovalTier { get; set; }
    public int DeviationSeverity { get; set; }
    public decimal TotalExposure { get; set; }
    public string LoanType { get; set; } = string.Empty;
    public string? MatchedRule { get; set; }
    public bool DocumentsComplete { get; set; }
    public List<string> MissingDocuments { get; set; } = new();
    public int? AssignedApproverId { get; set; }
    public string? AssignedApproverName { get; set; }
}
