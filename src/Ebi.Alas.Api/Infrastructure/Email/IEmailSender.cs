namespace Ebi.Alas.Api.Infrastructure.Email;

public interface IEmailSender
{
    Task SendAsync(
        string to,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken);
}
