namespace EBI.ALAS.Api.Features.Users;

/// <summary>
/// Represents a single row from the import Excel file.
/// </summary>
public record UserImportRow(
    int RowNumber,
    string? Username,
    string? FirstName,
    string? MiddleName,
    string? LastName,
    string? BranchCode,
    string? Role,
    string? JobTitle,
    string? CoveredBranches, // comma-separated for approvers
    string? Email,
    string? Phone
);

/// <summary>
/// Validation error for a specific row and field during import.
/// </summary>
public record UserImportValidationError(
    int RowNumber,
    string Field,
    string Error
);

/// <summary>
/// Result of a batch import operation.
/// </summary>
public record UserImportResult(
    int TotalRows,
    int SuccessfulImports,
    int FailedImports,
    List<UserImportValidationError> Errors,
    List<string> CreatedUsernames
);

/// <summary>
/// Parameters for exporting users to Excel.
/// </summary>
public record ExportUsersParameters(
    string? Search,
    string? Role,
    string? BranchCode,
    bool? IsActive
);
