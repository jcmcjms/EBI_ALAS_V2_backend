namespace EBI.ALAS.Api.Features.Loans;
public record LoanProductImportRow(
    int RowNumber,
    string? Code,
    string? Description,
    decimal? MinAmount,
    decimal? MaxAmount,
    int? MinTermDays,
    int? MaxTermDays,
    decimal? NotarialFee,
    decimal? DocStampFee,
    decimal? InsuranceFee,
    decimal? AdvanceInterestRate,
    decimal? ApplicationChargeRate,
    string? AmortizationMode,
    bool? ChargeAdvanceInterest,
    bool? IsRetired
);
public record LoanProductImportValidationError(
    int RowNumber,
    string Field,
    string Error
);
public record LoanProductImportResult(
    int TotalRows,
    int Created,
    int Updated,
    int Failed,
    List<LoanProductImportValidationError> Errors
);
public record ExportLoanProductsParameters(
    bool? IncludeRetired
);
