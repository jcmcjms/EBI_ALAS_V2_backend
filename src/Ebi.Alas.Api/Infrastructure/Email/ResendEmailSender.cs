using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Infrastructure.Email;

/// <summary>Sends mail via the Resend HTTP API (no SMTP/MailKit).</summary>
public sealed class ResendEmailSender(
    IOptions<EmailOptions> options,
    HttpClient http,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private static readonly Uri SendUri = new("https://api.resend.com/emails");

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

        var cfg = options.Value;
        if (string.IsNullOrWhiteSpace(cfg.ApiKey) || string.IsNullOrWhiteSpace(cfg.From))
        {
            throw new EmailSendException("Resend is not configured (ApiKey/From).");
        }

        var payload = JsonSerializer.Serialize(new
        {
            from = cfg.From,
            to = new[] { to },
            subject,
            text = textBody,
            html = htmlBody
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, SendUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Email.ResendFailed status={Status}",
                    (int)response.StatusCode);
                throw new EmailSendException($"Resend returned {(int)response.StatusCode}.");
            }
        }
        catch (EmailSendException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EmailSendException("Resend send failed.", ex);
        }

        logger.LogInformation("Email.Resent toDomain={Domain}", GetDomain(to));
    }

    private static string GetDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at <= 0 ? "invalid" : email[(at + 1)..];
    }
}
