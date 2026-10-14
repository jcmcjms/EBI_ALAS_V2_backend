using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Ebi.Alas.Api.Infrastructure.Email;

public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
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
        ArgumentException.ThrowIfNullOrWhiteSpace(textBody);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlBody);

        var smtp = options.Value;
        if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.From))
        {
            throw new EmailSendException("SMTP is not configured.");
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder
        {
            TextBody = textBody,
            HtmlBody = htmlBody
        }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(
                smtp.Host,
                smtp.Port,
                smtp.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
                cancellationToken);
            if (!string.IsNullOrEmpty(smtp.UserName))
            {
                await client.AuthenticateAsync(smtp.UserName, smtp.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex) when (ex is MailKit.ServiceNotConnectedException
            or MailKit.ServiceNotAuthenticatedException
            or MailKit.ProtocolException
            or System.Net.Sockets.SocketException
            or IOException
            or TimeoutException)
        {
            throw new EmailSendException("SMTP send failed.", ex);
        }

        // Log domain only — never the address local-part or message body.
        logger.LogInformation("Email.Sent toDomain={Domain}", GetDomain(to));
    }

    private static string GetDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at <= 0 ? "invalid" : email[(at + 1)..];
    }
}
