namespace EBI.ALAS.Api.Features.Users;
public record UserImportRow(
    int RowNumber,
    string? Username,
    string? FirstName,
    string? MiddleName,
    string? LastName,
    string? BranchCode,
    string? Role,
    string? JobTitle,
    string? CoveredBranches,
    string? Email,
    string? Phone
);
public record UserImportValidationError(
    int RowNumber,
    string Field,
    string Error
);
public record UserImportResult(
    int TotalRows,
    int SuccessfulImports,
    int FailedImports,
    List<UserImportValidationError> Errors,
    List<string> CreatedUsernames
);
public record ExportUsersParameters(
    string? Search,
    string? Role,
    string? BranchCode,
    bool? IsActive
);
