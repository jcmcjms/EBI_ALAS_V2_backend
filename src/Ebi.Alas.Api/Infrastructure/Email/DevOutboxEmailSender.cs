using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Infrastructure.Email;

/// <summary>Writes messages to a local outbox folder instead of SMTP. Development only.</summary>
public sealed class DevOutboxEmailSender(
    IOptions<EmailOptions> options,
    IHostEnvironment environment,
    ILogger<DevOutboxEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(
        string to,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        if (environment.IsProduction())
        {
            throw new EmailSendException("Dev outbox is not allowed in Production.");
        }

        var outbox = options.Value.DevOutboxPath;
        if (string.IsNullOrWhiteSpace(outbox))
        {
            outbox = Path.Combine(AppContext.BaseDirectory, "email-outbox");
        }

        Directory.CreateDirectory(outbox);
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.eml";
        var path = Path.Combine(outbox, fileName);

        var body = $"""
            To: {to}
            From: {options.Value.From}
            Subject: {subject}
            Date: {DateTimeOffset.UtcNow:O}
            X-ALAS-Dev-Outbox: true

            {textBody}

            --- html ---
            {htmlBody}
            """;

        await File.WriteAllTextAsync(path, body, cancellationToken);
        logger.LogInformation(
            "Email.DevOutbox file={FileName} toDomain={Domain}",
            fileName,
            GetDomain(to));
    }

    private static string GetDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at <= 0 ? "invalid" : email[(at + 1)..];
    }
}
