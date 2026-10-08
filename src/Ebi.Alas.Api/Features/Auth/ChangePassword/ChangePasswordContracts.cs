namespace Ebi.Alas.Api.Features.Auth.ChangePassword;

public sealed record ChangePasswordRequest
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    public required string CurrentPassword { get; init; }

    public required string NewPassword { get; init; }
}

public abstract record ChangePasswordOutcome
{
    public sealed record Success : ChangePasswordOutcome;

    public sealed record Failure(string Detail, int StatusCode) : ChangePasswordOutcome;

    public static ChangePasswordOutcome InvalidCurrentPassword() =>
        new Failure("Current password is incorrect.", StatusCodes.Status400BadRequest);

    public static ChangePasswordOutcome InvalidNewPassword(string detail) =>
        new Failure(detail, StatusCodes.Status400BadRequest);

    public static ChangePasswordOutcome UserNotFound() =>
        new Failure("Account not found.", StatusCodes.Status404NotFound);
}

