namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Represents a single row from the loan product import Excel file.
/// All fields are nullable so we can detect missing values and report
/// per-field validation errors rather than failing on the first null.
/// </summary>
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

/// <summary>
/// Validation error for a specific row and field during loan product import.
/// </summary>
public record LoanProductImportValidationError(
    int RowNumber,
    string Field,
    string Error
);

/// <summary>
/// Result of a batch loan product import operation.
/// Uses upsert semantics: existing codes are updated, new codes are created.
/// </summary>
public record LoanProductImportResult(
    int TotalRows,
    int Created,
    int Updated,
    int Failed,
    List<LoanProductImportValidationError> Errors
);

/// <summary>
/// Parameters for exporting loan products to Excel.
/// </summary>
public record ExportLoanProductsParameters(
    bool? IncludeRetired
);
