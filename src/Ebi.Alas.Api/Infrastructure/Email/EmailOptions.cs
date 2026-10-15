using System.ComponentModel.DataAnnotations;

namespace Ebi.Alas.Api.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Api:Email:Smtp";

    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    [Required]
    public string From { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool EnableSsl { get; set; } = true;

    /// <summary>EnterpriseEmailApi (default), Resend, Smtp, or DevOutbox (Development only).</summary>
    public string Provider { get; set; } = "EnterpriseEmailApi";

    /// <summary>API key: Enterprise Email API X-Api-Key, or Resend re_ key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Base URL of EnterpriseBank_EmailAPI, e.g. http://localhost:5151</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional AzureEmailConfigs Id in EnterpriseBank_EmailAPI.</summary>
    public int? SenderConfigId { get; set; }

    /// <summary>Directory for DevOutbox files. Defaults to {BaseDirectory}/email-outbox.</summary>
    public string? DevOutboxPath { get; set; }
}
