namespace Ebi.Alas.Api.Features.LoanProducts;

public sealed record LoanProductResponse(
    string Code,
    string Description,
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermDays,
    int MaxTermDays,
    decimal NotarialFee,
    decimal DocStampFee,
    decimal InsuranceFee,
    decimal AdvanceInterestRate,
    decimal ApplicationChargeRate,
    string AmortizationMode,
    bool ChargeAdvanceInterest,
    bool IsRetired,
    DateTimeOffset LastSyncedAt);

public sealed record UpdateLoanProductPolicyRequest(
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermDays,
    int MaxTermDays,
    decimal NotarialFee,
    decimal DocStampFee,
    decimal InsuranceFee,
    decimal AdvanceInterestRate);

public sealed record LoanProductSyncResult(
    int Added,
    int Updated,
    int Preserved,
    DateTimeOffset SyncedAt);

public sealed record LoanProductImportValidationError(
    int RowNumber,
    string Field,
    string Error);

public sealed record LoanProductImportResult(
    int TotalRows,
    int Created,
    int Updated,
    int Failed,
    IReadOnlyList<LoanProductImportValidationError> Errors);
