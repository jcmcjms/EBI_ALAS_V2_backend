using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Loans.DTOs;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

public static class GetLoanById
{
    public static void MapGetLoanByIdEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapGet("/{id:int}", async (
            int id,
            ILoanRepository loanRepository,
            AppDbContext db,
            IAuditLogger auditLogger,
            CancellationToken ct) =>
        {
            var loan = await loanRepository.GetByIdAsync(id, includeRelated: true);
            if (loan == null)
            {
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));
            }

            var lastAction = loan.Actions
                .OrderByDescending(a => a.ActionDate).ThenByDescending(a => a.Id)
                .FirstOrDefault();

            string? assignedApproverName = null;
            if (loan.AssignedApproverId is { } assigneeId)
            {
                var assignee = await db.Users.AsNoTracking()
                    .Where(u => u.Id == assigneeId)
                    .Select(u => new { u.FirstName, u.LastName })
                    .FirstOrDefaultAsync(ct);
                if (assignee is not null)
                    assignedApproverName = $"{assignee.FirstName} {assignee.LastName}";
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
                PolicyTermMonths = loan.PolicyTermMonths,
                ApprovalTermDays = loan.ApprovalTermDays,
                AnnualRatePercent = loan.AnnualRatePercent,
                NotarialFee = loan.NotarialFee,
                DocStamps = loan.DocStamps,
                Insurance = loan.Insurance,
                StandardNotarialFee = loan.StandardNotarialFee,
                StandardDocStamps = loan.StandardDocStamps,
                StandardInsurance = loan.StandardInsurance,
                StandardApplicationCharge = loan.StandardApplicationCharge,
                StandardAdvanceInterest = loan.StandardAdvanceInterest,
                TotalDeductions = loan.TotalDeductions,
                DeductionRate = loan.DeductionRate,
                GrossProceeds = loan.GrossProceeds,
                NetProceedsOnDS = loan.NetProceedsOnDS,
                NetProceedsToClient = loan.NetProceedsToClient,
                TotalExposure = loan.TotalExposure,
                MonthlyAmortization = loan.MonthlyAmortization,
                NetPayAfterDeduction = loan.NetPayAfterDeduction,
                GrossDisposableIncome = loan.GrossDisposableIncome,
                CapacityDeductions = loan.CapacityDeductions,
                NetDisposableIncome = loan.NetDisposableIncome,
                MaximumLoanableAmount = loan.MaximumLoanableAmount,
                AmortizationExceedsDisposable = loan.AmortizationExceedsDisposable,
                NthpBelowMinimum = loan.NthpBelowMinimum,
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
                LastActionByName = lastAction is not null
                    ? $"{lastAction.ActionByUser.FirstName} {lastAction.ActionByUser.LastName}"
                    : $"{loan.CreatedBy.FirstName} {loan.CreatedBy.LastName}",
                LastAction = lastAction?.Action,
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
                EvaluationVerdict = loan.Actions
                    .Where(a => a.Action == "EvaluatedRecommended" || a.Action == "EvaluatedNotRecommended")
                    .OrderByDescending(a => a.ActionDate).ThenByDescending(a => a.Id)
                    .Select(a => a.Action)
                    .FirstOrDefault(),
                LoanType = loan.LoanType,
                DeviationSeverity = (int)loan.DeviationSeverity,
                RequiredApprovalTier = loan.RequiredApprovalTier,
                AssignedApproverId = loan.AssignedApproverId,
                AssignedApproverName = assignedApproverName,
                AssignedAt = loan.AssignedAt,
                DocumentsCompleteAt = loan.DocumentsCompleteAt,
                IncompleteReturnStatus = loan.IncompleteReturnStatus,
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
    }
}
