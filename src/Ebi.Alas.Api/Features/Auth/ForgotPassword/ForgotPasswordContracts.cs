namespace Ebi.Alas.Api.Features.Auth.ForgotPassword;

public sealed record ForgotPasswordRequest
{
    public required string UserName { get; init; }
}

public sealed record ForgotPasswordResponse(string Message);

public abstract record ForgotPasswordOutcome
{
    public sealed record Success(string Message) : ForgotPasswordOutcome;

    public sealed record Failure(string Detail, int StatusCode) : ForgotPasswordOutcome;

    public static ForgotPasswordOutcome AccountNotFound() =>
        new Failure("Account not found.", StatusCodes.Status404NotFound);

    public static ForgotPasswordOutcome MissingEmail() =>
        new Failure("No email on file. Contact IT support.", StatusCodes.Status400BadRequest);

    public static ForgotPasswordOutcome CooldownActive() =>
        new Failure("A reset email was already sent. Try again later.", StatusCodes.Status429TooManyRequests);

    public static ForgotPasswordOutcome EmailSendFailed() =>
        new Failure("Unable to send email. Contact IT support.", StatusCodes.Status500InternalServerError);
}
