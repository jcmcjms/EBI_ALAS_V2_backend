using System.Security.Claims;
using System.Text.Json;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Time;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public class LoanSubmissionService : ILoanSubmissionService
{
    private const int MaxSequenceCollisions = 2;
    private const string IdempotencyIndexName = "IX_LoanSubmissionIdempotency_Key_User";

    private readonly ILoanRepository _loanRepository;
    private readonly IFormNumberGenerator _formNumberGenerator;
    private readonly ILoanWorkflowService _workflowService;
    private readonly IAuditLogger _auditLogger;
    private readonly ITimeProvider _timeProvider;

    public LoanSubmissionService(
        ILoanRepository loanRepository,
        IFormNumberGenerator formNumberGenerator,
        ILoanWorkflowService workflowService,
        IAuditLogger auditLogger,
        ITimeProvider timeProvider)
    {
        _loanRepository = loanRepository;
        _formNumberGenerator = formNumberGenerator;
        _workflowService = workflowService;
        _auditLogger = auditLogger;
        _timeProvider = timeProvider;
    }

    public async Task<(LoanSubmissionResponse Response, bool Replayed)> SubmitAsync(
        SubmitLoanApplicationRequest request,
        Guid idempotencyKey,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var userId = user.GetUserId();

        // 1 ── Replay guard: same key + same user ⇒ stored response, nothing written.
        var existing = await _loanRepository.GetIdempotencyRecordAsync(idempotencyKey, userId, ct);
        if (existing is not null)
        {
            var replayed = JsonSerializer.Deserialize<LoanSubmissionResponse>(existing.ResponseJson)!;
            return (replayed, true);
        }

        // 2 ── Server-derived truth. The client's branch/officer values are ignored.
        var branchCode = user.GetBranchId();
        var officer = await _loanRepository.GetOfficerDisplayNameAsync(userId, ct)
            ?? throw new ForbiddenAccessException("Unable to resolve the acting officer.");

        // Broken-access-control guard: an officer may only encode against
        // preloans of their own branch (the webloan lookup is already scoped
        // the same way; this closes the direct-POST bypass).
        if (request.Loans.Any(l => !string.Equals(l.BranchCode, branchCode, StringComparison.Ordinal))
            || (request.PreLoan is not null && !string.Equals(request.PreLoan.Bch, branchCode, StringComparison.Ordinal)))
        {
            throw new ForbiddenAccessException("Loan branch does not match the acting officer's branch.");
        }

        // 3 ── Workflow gate: role must be allowed to move Draft → ForRecommendation.
        var role = user.GetRole();
        if (!_workflowService.IsValidTransition("Draft", "ForRecommendation", role))
        {
            throw new InvalidWorkflowException("Draft", "ForRecommendation", role);
        }

        // 4 ── Persist, retrying only on LAM/group sequence collisions.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await PersistAsync(request, idempotencyKey, userId, branchCode, officer, ct);
            }
            catch (DbUpdateException ex) when (IsIdempotencyCollision(ex))
            {
                // Concurrent duplicate submit won the race: return its result.
                var record = await _loanRepository.GetIdempotencyRecordAsync(idempotencyKey, userId, ct);
                if (record is null) throw;
                return (JsonSerializer.Deserialize<LoanSubmissionResponse>(record.ResponseJson)!, true);
            }
            catch (DbUpdateException ex) when (attempt < MaxSequenceCollisions && IsUniqueViolation(ex))
            {
                // Another process minted the same LAM sequence between our
                // allocation and the insert — re-allocate and retry.
            }
        }
    }

    private async Task<(LoanSubmissionResponse Response, bool Replayed)> PersistAsync(
        SubmitLoanApplicationRequest request,
        Guid idempotencyKey,
        int userId,
        string branchCode,
        string officer,
        CancellationToken ct)
    {
        var groupNo = await _formNumberGenerator.GenerateGroupNumberAsync(ct);
        var lamIds = await _formNumberGenerator.GenerateFormNumbersAsync(request.Loans.Count, ct);
        var now = _timeProvider.UtcNow;

        var applications = request.Loans
            .Select((loan, i) => MapApplication(request, loan, groupNo, lamIds[i], branchCode, officer, userId, now))
            .ToList();

        var idempotency = new LoanSubmissionIdempotency
        {
            IdempotencyKey = idempotencyKey,
            UserId = userId,
            ResponseJson = string.Empty, // filled below, before the single SaveChanges
            CreatedAt = now,
        };

        // Initial response (ids=0; real PKs arrive after SaveChangesAsync).
        var response = new LoanSubmissionResponse
        {
            ApplicationGroupNo = groupNo,
            Loans = applications
                .Select((a, i) => new CreatedLoan
                {
                    Id = 0,
                    LamId = lamIds[i],
                    LoanNo = a.LoanNo,
                    ProductCode = a.ProductCode,
                    ProposedAmount = a.ProposedAmount,
                    Status = a.Status,
                })
                .ToList(),
        };

        // applications + idempotency row go in ONE transaction: a crash can
        // never leave applications without their replay guard (or vice versa).
        await _loanRepository.CreateSubmissionAsync(applications, idempotency, ct);

        // Stamp real ids onto the response, persist the JSON so a replay returns it verbatim.
        response = response with
        {
            Loans = applications
                .Select(a => new CreatedLoan
                {
                    Id = a.Id,
                    LamId = a.FormNumber,
                    LoanNo = a.LoanNo,
                    ProductCode = a.ProductCode,
                    ProposedAmount = a.ProposedAmount,
                    Status = a.Status,
                })
                .ToList(),
        };
        idempotency.ResponseJson = JsonSerializer.Serialize(response);
        await _loanRepository.UpdateIdempotencyResponseAsync(idempotency, ct);

        // Audit: creation + the encoder's submit transition, per loan.
        foreach (var application in applications)
        {
            await _auditLogger.LogActionAsync(application.Id, userId, "Created", null, "Draft",
                $"Loan application created (group {groupNo})");
            await _auditLogger.LogActionAsync(application.Id, userId, "StatusChanged", "Draft", "ForRecommendation",
                "Submitted for recommendation");
        }

        return (response, false);
    }

    private LoanApplication MapApplication(
        SubmitLoanApplicationRequest request,
        LoanSection loan,
        string groupNo,
        string lamId,
        string branchCode,
        string officer,
        int userId,
        DateTime now)
    {
        var client = request.Client;
        var p = loan.Parameters;

        return new LoanApplication
        {
            FormNumber = lamId,
            ApplicationGroupNo = groupNo,
            BranchCode = branchCode,

            CreationTypeCode = loan.CreationTypeCode,
            CreationTypeLabel = loan.CreationTypeLabel,
            RequestingOfficer = officer,
            Lai = request.BranchType.Lai,

            CisId = client.CisId,
            FirstName = client.FirstName,
            MiddleName = client.MiddleName,
            LastName = client.LastName,
            Suffix = client.Suffix,
            Birthdate = ParseIsoDate(client.Birthdate),
            Address = client.Address,
            Agency = client.Agency,
            Position = client.Position,
            EmployeeId = client.EmployeeId,
            NetTakeHomePay = client.NetTakeHomePay,
            LengthOfService = client.LengthOfService,
            Region = client.Region,
            DivisionCode = client.DivisionCode,
            StationCode = client.StationCode,
            MisAgency = client.MisAgency,
            School = client.School,
            Referrer = client.Referrer,

            LoanNo = loan.LoanNo,
            ProductCode = loan.ProductCode,
            Product = p.Product,
            Purpose = p.Purpose,
            ProposedAmount = p.ProposedAmount,
            TermDays = p.Term,
            InterestRate = p.InterestRate,
            NthpDate = ParseIsoDate(p.NthpDate),
            NotarialFee = p.NotarialFee,
            DocStamps = p.DocStamps,
            Insurance = p.Insurance,
            StandardNotarialFee = p.StandardFeesSnapshot.NotarialFee,
            StandardDocStamps = p.StandardFeesSnapshot.DocStamps,
            StandardInsurance = p.StandardFeesSnapshot.Insurance,

            VerificationFindings = request.Verification.Findings,
            HasDeviations = request.Deviations.HasDeviations,
            DeviationDetails = request.Deviations.DeviationDetails.ToList(),
            DeviationJustifications = new Dictionary<string, string>(request.Deviations.DeviationJustifications),
            Remarks = request.Deviations.Remarks,
            AoRecommendation = request.Deviations.AoRecommendation,
            OtherRemarks = request.Deviations.OtherRemarks,
            FeeDeviationJustification = request.Deviations.FeeDeviationJustification,

            Status = "ForRecommendation",
            ApplicationDate = now,
            LastActionDate = now,
            CreatedById = userId,

            WebLoanCisNo = client.CisId,
            WebLoanBranchCode = loan.BranchCode,
            WebLoanAccountNumbers = request.PreLoan is null ? [] : [request.PreLoan.AccountNo],
            WebLoanPnNumbers = [loan.LoanNo],
            WebLoanLastSyncedAt = now,
            PreLoanId = request.PreLoan?.Id,
            PreLoanFormNumber = request.PreLoan?.FormNumber,

            OutstandingLoans = request.OutstandingLoans.Select(o => new OutstandingLoan
            {
                Pn = o.Pn,
                PrincipalBalance = o.PrincipalBalance,
                Amortization = o.Amortization,
                OutstandingBalance = o.OutstandingBalance,
                DateGranted = ParseIsoDate(o.DateGranted),
                DateMaturity = ParseIsoDate(o.DateMaturity),
                Status = o.Status,
                ProductWithDescription = o.ProductWithDescription,
            }).ToList(),
            EbiReloans = request.EbiReloans.Select(e => new EbiReloan
            {
                Pn = e.Pn,
                Name = e.Name,
                ExistingDeduction = e.ExistingDeduction,
                OutstandingBalance = e.OutstandingBalance,
                PayToClose = e.PayToClose,
            }).ToList(),
            BuyOuts = request.BuyOuts.Select(b => new BuyOut
            {
                Pn = b.Pn,
                Name = b.Name,
                Amortization = b.Amortization,
                OutstandingBalance = b.OutstandingBalance,
            }).ToList(),
            IncomingLoans = request.IncomingLoans.Select(i => new IncomingLoan
            {
                Name = i.Name,
                Deductions = i.Deductions,
                Remarks = i.Remarks,
            }).ToList(),
        };
    }

    private static DateOnly? ParseIsoDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", out var parsed) ? parsed : null;

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    private static bool IsIdempotencyCollision(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains(IdempotencyIndexName, StringComparison.Ordinal);
}