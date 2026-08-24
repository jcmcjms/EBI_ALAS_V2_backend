using Alas.Domain.Entities;

namespace Alas.Application.Loans;

public sealed record LoanListItemDto(
    Guid LoanId,
    string LoanNumber,
    string BorrowerName,
    decimal PrincipalAmount,
    decimal InterestRate,
    int TermMonths,
    LoanStatus Status,
    string? BranchId,
    DateTimeOffset CreatedUtc);

public sealed record LoanDetailDto(
    Guid LoanId,
    string LoanNumber,
    string BorrowerName,
    string? BorrowerContact,
    decimal PrincipalAmount,
    decimal InterestRate,
    int TermMonths,
    string? Purpose,
    string? BranchId,
    LoanStatus Status,
    string CreatedByUserName,
    string? ApprovedByUserName,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ApprovedUtc,
    DateTimeOffset? DisbursedUtc,
    string? Remarks,
    string? RejectionReason);

public sealed record CreateLoanRequest(
    string BorrowerName,
    string? BorrowerContact,
    decimal PrincipalAmount,
    decimal InterestRate,
    int TermMonths,
    string? Purpose,
    string? BranchId,
    string? Remarks);

public sealed record ApproveLoanRequest(
    string? Remarks);

public sealed record RejectLoanRequest(
    string RejectionReason);

public sealed record LoanMonitorDto(
    int TotalLoans,
    int DraftCount,
    int PendingReviewCount,
    int PendingApprovalCount,
    int ApprovedCount,
    int DisbursedCount,
    int RejectedCount,
    int CancelledCount,
    decimal TotalPrincipal,
    decimal DisbursedPrincipal);
