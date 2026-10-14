using System.ComponentModel.DataAnnotations;

namespace Ebi.Alas.Api.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Api:Email:Smtp";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    [Required]
    public string From { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool EnableSsl { get; set; } = true;
}
