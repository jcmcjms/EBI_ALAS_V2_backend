using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Infrastructure.Email;

/// <summary>
/// Sends mail through the bank's EnterpriseBank_EmailAPI (Microsoft Graph).
/// </summary>
public sealed class EnterpriseEmailApiSender(
    IOptions<EmailOptions> options,
    HttpClient http,
    ILogger<EnterpriseEmailApiSender> logger) : IEmailSender
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
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlBody);

        var cfg = options.Value;
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl) || string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            throw new EmailSendException("Enterprise Email API is not configured (BaseUrl/ApiKey).");
        }

        var endpoint = cfg.BaseUrl.TrimEnd('/') + "/api/Email/SendEmail";
        var payload = JsonSerializer.Serialize(new
        {
            toEmail = to,
            subject,
            body = htmlBody,
            senderConfigId = cfg.SenderConfigId
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("X-Api-Key", cfg.ApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Email.EnterpriseApiFailed status={Status}",
                    (int)response.StatusCode);
                throw new EmailSendException($"Enterprise Email API returned {(int)response.StatusCode}.");
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
            throw new EmailSendException("Enterprise Email API send failed.", ex);
        }

        logger.LogInformation("Email.EnterpriseApiSent toDomain={Domain}", GetDomain(to));
    }

    private static string GetDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at <= 0 ? "invalid" : email[(at + 1)..];
    }
}
