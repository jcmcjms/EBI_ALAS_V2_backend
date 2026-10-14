using Ebi.Alas.Api.Infrastructure.Email;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Auth.ForgotPassword;

public sealed class ForgotPasswordHandler(
    AlasDbContext db,
    PasswordHasher passwordHasher,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<ForgotPasswordHandler> logger)
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    public async Task<ForgotPasswordOutcome> HandleAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserName);

        var userName = request.UserName.Trim();
        logger.LogInformation("{Event} user={UserName}", AuthEvents.PasswordResetRequested, userName);

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == userName, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("{Event} user={UserName} reason=not_found", AuthEvents.PasswordResetFailed, userName);
            return ForgotPasswordOutcome.AccountNotFound();
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogWarning("{Event} userId={UserId} reason=missing_email", AuthEvents.PasswordResetFailed, user.Id);
            return ForgotPasswordOutcome.MissingEmail();
        }

        var now = timeProvider.GetUtcNow();
        if (user.IsPasswordResetCooldownActive(now, Cooldown))
        {
            logger.LogWarning("{Event} userId={UserId} reason=cooldown", AuthEvents.PasswordResetFailed, user.Id);
            return ForgotPasswordOutcome.CooldownActive();
        }

        var temporaryPassword = TemporaryPasswordGenerator.Generate();

        try
        {
            await emailSender.SendAsync(
                user.Email,
                "ALAS temporary password",
                BuildTextBody(user.UserName, temporaryPassword),
                BuildHtmlBody(user.UserName, temporaryPassword),
                cancellationToken);
        }
        catch (EmailSendException ex)
        {
            logger.LogError(ex, "{Event} userId={UserId} reason=send_failed", AuthEvents.PasswordResetFailed, user.Id);
            return ForgotPasswordOutcome.EmailSendFailed();
        }

        // Persist only after a successful send so a failed email cannot lock the account.
        user.SetPasswordHash(
            passwordHasher.Hash(temporaryPassword),
            mustChangePassword: true,
            now: now);
        user.MarkPasswordResetSent(now);

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == user.Id)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(now, Guid.Empty);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("{Event} userId={UserId}", AuthEvents.PasswordResetEmailSent, user.Id);
        return new ForgotPasswordOutcome.Success("Temporary password sent. Check your email, then sign in.");
    }

    private static string BuildTextBody(string userName, string temporaryPassword) =>
        $"""
        ALAS password reset

        Username: {userName}
        Temporary password: {temporaryPassword}

        Sign in with this temporary password. You will be asked to choose a new password immediately.
        If you did not request this reset, contact IT support.
        """;

    private static string BuildHtmlBody(string userName, string temporaryPassword) =>
        $"""
        <p>ALAS password reset</p>
        <p>Username: <strong>{System.Net.WebUtility.HtmlEncode(userName)}</strong><br/>
        Temporary password: <strong>{System.Net.WebUtility.HtmlEncode(temporaryPassword)}</strong></p>
        <p>Sign in with this temporary password. You will be asked to choose a new password immediately.</p>
        <p>If you did not request this reset, contact IT support.</p>
        """;
}
