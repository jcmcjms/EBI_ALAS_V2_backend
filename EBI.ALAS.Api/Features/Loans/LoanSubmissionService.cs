using System.Security.Claims;
using System.Text.Json;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Exceptions;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Loans.Computation;
using EBI.ALAS.Api.Features.Notifications;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public class LoanSubmissionService(
    ILoanRepository loanRepository,
    ILamIdGenerator lamIdGenerator,
    ILoanWorkflowService workflowService,
    IAuditLogger auditLogger,
    ITimeProvider timeProvider,
    INotificationService notificationService,
    IRealtimeNotificationService realtimeService,
    ILoanComputationService computationService,
    ILoanProductRepository productRepository,
    IWorkflowConfiguration workflowConfig,
    IWorkflowQueueService queueService,
    IDocumentGateService documentGate) : ILoanSubmissionService
{
    private const int MaxSequenceCollisions = 2;
    private const string IdempotencyIndexName = "IX_LoanSubmissionIdempotency_Key_User";

    public async Task<(LoanSubmissionResponse Response, bool Replayed)> SubmitAsync(
        SubmitLoanApplicationRequest request,
        Guid idempotencyKey,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var userId = user.GetUserId();

        // 1 ── Replay guard: same key + same user ⇒ stored response, nothing written.
        var existing = await loanRepository.GetIdempotencyRecordAsync(idempotencyKey, userId, ct);
        if (existing is not null)
        {
            var replayed = JsonSerializer.Deserialize<LoanSubmissionResponse>(existing.ResponseJson)!;
            return (replayed, true);
        }

        // 2 ── Server-derived branch. The client's branch value is overwritten.
        var branchCode = user.GetBranchId();

        // Broken-access-control guard: an officer may only encode against
        // preloans of their own branch (the webloan lookup is already scoped
        // the same way; this closes the direct-POST bypass).
        if (request.Loans.Any(l => !string.Equals(l.BranchCode, branchCode, StringComparison.Ordinal))
            || (request.PreLoan is not null && !string.Equals(request.PreLoan.Bch, branchCode, StringComparison.Ordinal)))
        {
            throw new ForbiddenAccessException("Loan branch does not match the acting officer's branch.");
        }

        // 3 ── Workflow gate: role must be allowed to move Draft → initial status.
        var role = user.GetRole();
        var initialStatus = workflowService.InitialStatus;
        if (!workflowService.IsValidTransition("Draft", initialStatus, role))
        {
            throw new InvalidWorkflowException("Draft", initialStatus, role);
        }

        // 4 ── Persist, retrying only on LAM/group sequence collisions.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await PersistAsync(request, idempotencyKey, userId, branchCode, ct);
            }
            catch (DbUpdateException ex) when (IsIdempotencyCollision(ex))
            {
                // Concurrent duplicate submit won the race: return its result.
                var record = await loanRepository.GetIdempotencyRecordAsync(idempotencyKey, userId, ct);
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
        CancellationToken ct)
    {
        var groupNo = await lamIdGenerator.GenerateGroupNumberAsync(ct);
        var lamIds = await lamIdGenerator.GenerateLamIdsAsync(request.Loans.Count, ct);
        var now = timeProvider.UtcNow;

        // Compute metrics before persisting
        var applications = new List<LoanApplication>();

        foreach (var (loan, i) in request.Loans.Select((l, i) => (l, i)))
        {
            var application = MapApplication(request, loan, groupNo, lamIds[i], branchCode, userId, now);

            // Recompute all metrics server-side (never trust the client).
            var product = await productRepository.GetByCodeAsync(loan.ProductCode, ct);
            if (product is not null)
            {
                var productConfig = LoanProductComputationConfig.FromEntity(
                    product, loan.Parameters.InterestRate, loan.Parameters.Term);

                var defaultFees = computationService.ComputeExpectedFees(productConfig, loan.Parameters.ProposedAmount);
                var appliedFees = new LoanFees(
                    ApplicationCharge: defaultFees.ApplicationCharge, // not AO-overridable
                    DocStamp: loan.Parameters.DocStamps,
                    NotarialFee: loan.Parameters.NotarialFee,
                    Insurance: loan.Parameters.Insurance,
                    AdvanceInterest: defaultFees.AdvanceInterest); // not AO-overridable

                var results = computationService.ComputeLoanMetrics(new LoanComputationInput(
                    ProposedAmount: loan.Parameters.ProposedAmount,
                    Product: productConfig,
                    Fees: appliedFees,
                    NetTakeHomePay: request.Client.NetTakeHomePay ?? 0m,
                    MinimumNthp: workflowConfig.MinimumNthp,
                    OutstandingPrincipalBalances: request.OutstandingLoans.Select(o => o.PrincipalBalance).ToList(),
                    Reloans: loan.EbiReloans.Select(e => new ObligationRow(e.ExistingDeduction, e.OutstandingBalance)).ToList(),
                    BuyOuts: loan.BuyOuts.Select(b => new ObligationRow(b.Amortization, b.OutstandingBalance)).ToList(),
                    IncomingDeductions: loan.IncomingLoans.Select(i => i.Deductions).ToList()));

                // Persist computed snapshot (authoritative — overwrites any client values).
                application.TotalDeductions = results.TotalDeductions;
                application.DeductionRate = results.DeductionRate;
                application.GrossProceeds = results.GrossProceeds;
                application.NetProceedsOnDS = results.NetProceedsOnDS;
                application.NetProceedsToClient = results.NetProceedsToClient;
                application.TotalExposure = results.TotalExposure;
                application.MonthlyAmortization = results.MonthlyAmortization;
                application.NetPayAfterDeduction = results.NetPayAfterDeduction;
                application.GrossDisposableIncome = results.GrossDisposableIncome;
                application.CapacityDeductions = results.CapacityDeductions;
                application.NetDisposableIncome = results.NetDisposableIncome;
                application.MaximumLoanableAmount = results.MaximumLoanableAmount;
                application.AmortizationExceedsDisposable = results.AmortizationExceedsDisposable;
                application.NthpBelowMinimum = results.NthpBelowMinimum;

                // Capacity gates
                // AmortizationExceedsDisposable and NthpBelowMinimum gates
                // are computed and persisted for UI display but no longer
                // block submission.
            }

            applications.Add(application);
        }

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
        await loanRepository.CreateSubmissionAsync(applications, idempotency, ct);

        // Enqueue each loan into its initial review desk
        // The document gate checks completeness first: incomplete loans are
        // auto-held in ForIncompleteDocuments and queue themselves there.
        foreach (var application in applications)
        {
            if (!await documentGate.HoldIfIncompleteAsync(application, workflowService.InitialStatus, userId, ct))
                await queueService.EnqueueAsync(application, workflowService.InitialStatus, ct);
        }

        // Stamp real ids onto the response, persist the JSON so a replay returns it verbatim.
        response = response with
        {
            Loans = applications
                .Select(a => new CreatedLoan
                {
                    Id = a.Id,
                    LamId = a.LamId,
                    LoanNo = a.LoanNo,
                    ProductCode = a.ProductCode,
                    ProposedAmount = a.ProposedAmount,
                    Status = a.Status,
                })
                .ToList(),
        };
        idempotency.ResponseJson = JsonSerializer.Serialize(response);
        await loanRepository.UpdateIdempotencyResponseAsync(idempotency, ct);

        // Audit: creation + the encoder's submit transition, per loan.
        foreach (var application in applications)
        {
            await auditLogger.LogActionAsync(application.Id, userId, "Created", null, "Draft",
                $"Loan application created (group {groupNo})");
            await auditLogger.LogActionAsync(application.Id, userId, "StatusChanged", "Draft", workflowService.InitialStatus,
                workflowService.InitialStatus == "ForRecommendation"
                    ? "Submitted for recommendation"
                    : "Submitted for evaluation");
        }

        // Notify the branch's recommenders (or evaluators when recommender
        // step is skipped) that a new application group is waiting for
        // their review.
        await NotifySubmissionRecipientsAsync(
            branchCode, groupNo, applications, workflowService.RequireRecommendation, ct);

        // Dashboard real-time refresh — new submission shifts KPIs and pending queue
        await realtimeService.NotifyDashboardUpdateAsync(branchCode);

        return (response, false);
    }

    /// <summary>
    /// Notifies the appropriate branch users (recommenders or evaluators)
    /// about a new loan application submission.
    /// Extracted from PersistAsync to follow Single Responsibility Principle.
    /// </summary>
    private async Task NotifySubmissionRecipientsAsync(
        string branchCode,
        string groupNo,
        IReadOnlyList<LoanApplication> applications,
        bool requireRecommendation,
        CancellationToken ct)
    {
        var firstLoan = applications.First();
        var clientName = $"{firstLoan.FirstName} {firstLoan.LastName}";

        if (requireRecommendation)
        {
            var recommenders = await loanRepository.GetUsersByRoleAndBranchAsync(
                Roles.Recommender, branchCode, ct);

            foreach (var recommender in recommenders)
            {
                var message = $"Application group {groupNo} for {clientName} has been submitted for recommendation.";
                await notificationService.CreateAsync(
                    recommender.Id, "New Loan Application Submitted", message, "/loans/monitoring");
                await realtimeService.NotifyUserAsync(
                    recommender.Id, "New Loan Application Submitted", message, "/loans/monitoring");
            }
        }
        else
        {
            var evaluators = await loanRepository.GetUsersByRoleAndBranchAsync(
                Roles.Evaluator, branchCode, ct);

            foreach (var evaluator in evaluators)
            {
                var message = $"Application group {groupNo} for {clientName} has been submitted for evaluation.";
                await notificationService.CreateAsync(
                    evaluator.Id, "New Loan Application Submitted", message, "/loans/monitoring");
                await realtimeService.NotifyUserAsync(
                    evaluator.Id, "New Loan Application Submitted", message, "/loans/monitoring");
            }
        }
    }

    private LoanApplication MapApplication(
        SubmitLoanApplicationRequest request,
        LoanSection loan,
        string groupNo,
        string lamId,
        string branchCode,
        int userId,
        DateTime now)
    {
        var client = request.Client;
        var p = loan.Parameters;

        return new LoanApplication
        {
            LamId = lamId,
            ApplicationGroupNo = groupNo,
            BranchCode = branchCode,

            CreationTypeCode = loan.CreationTypeCode,
            CreationTypeLabel = loan.CreationTypeLabel,
            RequestingOfficer = request.BranchType.RequestingOfficer,
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
            PolicyTermMonths = p.PolicyTermMonths,
            ApprovalTermDays = ApprovalFormConventions.ResolveApprovalTermDays(p.Term, p.PolicyTermMonths),
            AnnualRatePercent = ApprovalFormConventions.ToAnnualRatePercent(p.InterestRate),
            NthpDate = ParseIsoDate(p.NthpDate),
            NotarialFee = p.NotarialFee,
            DocStamps = p.DocStamps,
            Insurance = p.Insurance,
            StandardNotarialFee = p.StandardFeesSnapshot.NotarialFee,
            StandardDocStamps = p.StandardFeesSnapshot.DocStamps,
            StandardInsurance = p.StandardFeesSnapshot.Insurance,
            StandardApplicationCharge = p.StandardFeesSnapshot.ApplicationCharge,
            StandardAdvanceInterest = p.StandardFeesSnapshot.AdvanceInterest,

            VerificationFindings = loan.Verification.Findings,
            HasDeviations = loan.Deviations.HasDeviations,
            DeviationDetails = loan.Deviations.DeviationDetails.ToList(),
            DeviationJustifications = new Dictionary<string, string>(loan.Deviations.DeviationJustifications),
            Remarks = loan.Deviations.Remarks,
            AoRecommendation = loan.Deviations.AoRecommendation,
            OtherRemarks = loan.Deviations.OtherRemarks,
            FeeDeviationJustification = loan.Deviations.FeeDeviationJustification,

            Status = workflowService.InitialStatus,
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
            EbiReloans = loan.EbiReloans.Select(e => new EbiReloan
            {
                Pn = e.Pn,
                Name = e.Name,
                ExistingDeduction = e.ExistingDeduction,
                OutstandingBalance = e.OutstandingBalance,
                PayToClose = e.PayToClose,
            }).ToList(),
            BuyOuts = loan.BuyOuts.Select(b => new BuyOut
            {
                Pn = b.Pn,
                Name = b.Name,
                Amortization = b.Amortization,
                OutstandingBalance = b.OutstandingBalance,
            }).ToList(),
            IncomingLoans = loan.IncomingLoans.Select(i => new IncomingLoan
            {
                Name = i.Name,
                Deductions = i.Deductions,
                Remarks = i.Remarks,
            }).ToList(),

            Deviations = BuildDeviationRows(loan.Deviations),
        };
    }

    /// <summary>
    /// Normalizes the deviation snapshot into child rows at submission time so
    /// reviewers' remarks can key off a stable FK. Runs inside the same
    /// SaveChanges as the application — a loan can never exist without its
    /// deviation threads.
    /// </summary>
    private static List<LoanDeviation> BuildDeviationRows(DeviationsSection deviations)
    {
        var rows = deviations.DeviationDetails
            .Select((reason, i) => new LoanDeviation
            {
                ReasonText = reason,
                EncoderJustification = deviations.DeviationJustifications.TryGetValue(reason, out var just)
                    ? just
                    : string.Empty,
                SortOrder = i,
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(deviations.FeeDeviationJustification))
        {
            rows.Add(new LoanDeviation
            {
                ReasonText = LoanDeviation.FeeOverrideReason,
                EncoderJustification = deviations.FeeDeviationJustification,
                SortOrder = 999,
                IsFeeOverride = true,
            });
        }

        return rows;
    }

    private static DateOnly? ParseIsoDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", out var parsed) ? parsed : null;

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    private static bool IsIdempotencyCollision(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains(IdempotencyIndexName, StringComparison.Ordinal);
}