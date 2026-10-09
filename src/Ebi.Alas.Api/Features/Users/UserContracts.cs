using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Features.Users;

public sealed record UserResponse(
    Guid Id,
    string UserName,
    string FullName,
    string Email,
    string BranchId,
    string Role,
    string Status,
    bool MustChangePassword,
    DateTimeOffset CreatedAt);

public sealed record CreateUserRequest(
    string UserName,
    string Password,
    string FullName,
    string? Email,
    string BranchId,
    UserRole Role);

public sealed record UpdateUserRequest(
    string FullName,
    string? Email,
    string BranchId);

public sealed record CreateUserResponse(UserResponse User);

public abstract record CreateUserOutcome
{
    public sealed record Success(UserResponse User) : CreateUserOutcome;

    public sealed record Failure(string Detail, int StatusCode) : CreateUserOutcome;

    public static CreateUserOutcome UsernameTaken(string userName) =>
        new Failure($"User '{userName}' already exists.", StatusCodes.Status409Conflict);

    public static CreateUserOutcome InvalidPassword(string detail) =>
        new Failure(detail, StatusCodes.Status400BadRequest);
}
