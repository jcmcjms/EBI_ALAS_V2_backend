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

    /// <summary>Resend (default), Smtp, or DevOutbox (Development only).</summary>
    public string Provider { get; set; } = "Resend";

    /// <summary>Resend API key (re_...). Required when Provider=Resend.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Directory for DevOutbox files. Defaults to {BaseDirectory}/email-outbox.</summary>
    public string? DevOutboxPath { get; set; }
}
