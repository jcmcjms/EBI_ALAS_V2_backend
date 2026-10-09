namespace Ebi.Alas.Api.Features.Users.ImportUsers;

public sealed record ImportUsersValidationError(int RowNumber, string Field, string Error);

public sealed record ImportUsersCredential(string Username, string TemporaryPassword);

public sealed record ImportUsersResult(
    int TotalRows,
    int SuccessfulImports,
    int FailedImports,
    IReadOnlyList<ImportUsersValidationError> Errors,
    IReadOnlyList<string> CreatedUsernames,
    IReadOnlyList<ImportUsersCredential> CreatedCredentials);
