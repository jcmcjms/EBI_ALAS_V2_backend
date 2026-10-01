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
    IWorkflowQueueService queueService) : ILoanSubmissionService
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
        var existing = await loanRepository.GetIdempotencyRecordAsync(idempotencyKey, userId, ct);
        if (existing is not null)
        {
            var replayed = JsonSerializer.Deserialize<LoanSubmissionResponse>(existing.ResponseJson)!;
            return (replayed, true);
        }
        var branchCode = user.GetBranchId();
        if (request.Loans.Any(l => !string.Equals(l.BranchCode, branchCode, StringComparison.Ordinal))
            || (request.PreLoan is not null && !string.Equals(request.PreLoan.Bch, branchCode, StringComparison.Ordinal)))
        {
            throw new ForbiddenAccessException("Loan branch does not match the acting officer's branch.");
        }
        var role = user.GetRole();
        var initialStatus = workflowService.InitialStatus;
        if (!workflowService.IsValidTransition("Draft", initialStatus, role))
        {
            throw new InvalidWorkflowException("Draft", initialStatus, role);
        }
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await PersistAsync(request, idempotencyKey, userId, branchCode, ct);
            }
            catch (DbUpdateException ex) when (IsIdempotencyCollision(ex))
            {
                var record = await loanRepository.GetIdempotencyRecordAsync(idempotencyKey, userId, ct);
                if (record is null) throw;
                return (JsonSerializer.Deserialize<LoanSubmissionResponse>(record.ResponseJson)!, true);
            }
            catch (DbUpdateException ex) when (attempt < MaxSequenceCollisions && IsUniqueViolation(ex))
            {
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
        var applications = new List<LoanApplication>();
        foreach (var (loan, i) in request.Loans.Select((l, i) => (l, i)))
        {
            var application = MapApplication(request, loan, groupNo, lamIds[i], branchCode, userId, now);
            var product = await productRepository.GetByCodeAsync(loan.ProductCode, ct);
            if (product is not null)
            {
                var productConfig = LoanProductComputationConfig.FromEntity(
                    product, loan.Parameters.InterestRate, loan.Parameters.Term);
                var defaultFees = computationService.ComputeExpectedFees(productConfig, loan.Parameters.ProposedAmount);
                var appliedFees = new LoanFees(
                    ApplicationCharge: defaultFees.ApplicationCharge,
                    DocStamp: loan.Parameters.DocStamps,
                    NotarialFee: loan.Parameters.NotarialFee,
                    Insurance: loan.Parameters.Insurance,
                    AdvanceInterest: defaultFees.AdvanceInterest);
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
            }
            applications.Add(application);
        }
        var idempotency = new LoanSubmissionIdempotency
        {
            IdempotencyKey = idempotencyKey,
            UserId = userId,
            ResponseJson = string.Empty,
            CreatedAt = now,
        };
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
        await loanRepository.CreateSubmissionAsync(applications, idempotency, ct);
        foreach (var application in applications)
        {
            await queueService.EnqueueAsync(application, workflowService.InitialStatus, ct);
        }
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
        foreach (var application in applications)
        {
            await auditLogger.LogActionAsync(application.Id, userId, "Created", null, "Draft",
                $"Loan application created (group {groupNo})");
            await auditLogger.LogActionAsync(application.Id, userId, "StatusChanged", "Draft", workflowService.InitialStatus,
                workflowService.InitialStatus == "ForRecommendation"
                    ? "Submitted for recommendation"
                    : "Submitted for evaluation");
        }
        await NotifySubmissionRecipientsAsync(
            branchCode, groupNo, applications, workflowService.RequireRecommendation, ct);
        await realtimeService.NotifyDashboardUpdateAsync(branchCode);
        return (response, false);
    }
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
        var deviationRemarks = NormalizeJustificationSnapshot(loan.Deviations);
        return new LoanApplication
        {
            LamId = lamId,
            ApplicationGroupNo = groupNo,
            BranchCode = branchCode,
            CreationTypeCode = loan.CreationTypeCode,
            CreationTypeLabel = loan.CreationTypeLabel,
            LoanType = loan.CreationTypeCode == 1 ? "Renewal" : "New",
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
            CDocStamp = loan.CDocStamp,
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
            DeviationDetails = loan.Deviations.DeviationDetails.Distinct(StringComparer.Ordinal).ToList(),
            DeviationJustifications = deviationRemarks,
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
            Deviations = BuildDeviationRows(loan.Deviations, deviationRemarks),
        };
    }
    private static List<LoanDeviation> BuildDeviationRows(
        DeviationsSection deviations,
        IReadOnlyDictionary<string, string> justificationSnapshot)
    {
        var rows = deviations.DeviationDetails
            .Distinct(StringComparer.Ordinal)
            .Select((reason, i) => new LoanDeviation
            {
                ReasonText = reason,
                EncoderJustification = justificationSnapshot.TryGetValue(reason, out var just)
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
                EncoderJustification = deviations.FeeDeviationJustification.Trim(),
                SortOrder = 999,
                IsFeeOverride = true,
            });
        }
        return rows;
    }
    private static Dictionary<string, string> NormalizeJustificationSnapshot(DeviationsSection deviations)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reason in deviations.DeviationDetails)
        {
            if (snapshot.ContainsKey(reason)) continue;
            snapshot[reason] = deviations.DeviationJustifications.TryGetValue(reason, out var remark)
                ? remark.Trim()
                : string.Empty;
        }
        return snapshot;
    }
    private static DateOnly? ParseIsoDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", out var parsed) ? parsed : null;
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
    private static bool IsIdempotencyCollision(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains(IdempotencyIndexName, StringComparison.Ordinal);
}
