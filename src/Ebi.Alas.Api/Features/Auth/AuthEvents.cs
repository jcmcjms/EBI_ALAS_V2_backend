namespace Ebi.Alas.Api.Features.Auth;

public static class AuthEvents
{
    public const string LoginSucceeded = "Auth.LoginSucceeded";
    public const string LoginFailed = "Auth.LoginFailed";
    public const string RefreshSucceeded = "Auth.RefreshSucceeded";
    public const string RefreshFailed = "Auth.RefreshFailed";
    public const string PasswordChanged = "Auth.PasswordChanged";
    public const string PasswordResetRequested = "Auth.PasswordResetRequested";
    public const string PasswordResetEmailSent = "Auth.PasswordResetEmailSent";
    public const string PasswordResetFailed = "Auth.PasswordResetFailed";
    public const string Logout = "Auth.Logout";
}
