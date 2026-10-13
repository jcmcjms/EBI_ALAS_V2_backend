using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Features.LoanApplications;

public sealed record LoanResponse(
    Guid Id,
    string LamId,
    string ApplicationGroupNo,
    string ClientName,
    string BranchId,
    string ProductCode,
    string Product,
    string LoanType,
    string Status,
    decimal Principal,
    int TermDays,
    decimal InterestRate,
    decimal TotalInterest,
    decimal TotalDeductions,
    decimal NetProceeds,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset LastActionDate,
    Guid? LastActionById,
    string? LastActionVerb);

/// <summary>One selected loan in a multi-loan application submission.</summary>
public sealed record SubmitLoanLine(
    string ProductCode,
    string Product,
    decimal Principal,
    int TermDays,
    decimal InterestRatePerMonth,
    decimal ServiceFee = 0,
    decimal InsuranceFee = 0,
    decimal OtherDeductions = 0);

/// <summary>
/// Multi-loan submission: one client application group with one or more loans,
/// matching the reference create-loan contract.
/// </summary>
public sealed record SubmitLoanApplicationsRequest(
    string? ApplicationGroupNo,
    string ClientName,
    string BranchId,
    LoanType LoanType,
    IReadOnlyList<SubmitLoanLine> Loans);

public sealed record CreatedLoanSummary(
    Guid Id,
    string LamId,
    string ProductCode,
    string Product,
    decimal Principal,
    string Status);

public sealed record LoanSubmissionResponse(
    string ApplicationGroupNo,
    IReadOnlyList<CreatedLoanSummary> Loans);
