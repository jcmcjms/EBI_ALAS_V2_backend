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

public sealed record SubmitLoanRequest(
    string? ApplicationGroupNo,
    string ClientName,
    string BranchId,
    string ProductCode,
    string Product,
    LoanType LoanType,
    decimal Principal,
    int TermDays,
    decimal InterestRatePerMonth,
    decimal ServiceFee = 0,
    decimal InsuranceFee = 0,
    decimal OtherDeductions = 0);
