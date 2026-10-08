using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Ebi.Alas.Api.Tests;

/// <summary>
/// Applies host settings from dotnet user-secrets when present.
/// Never hardcodes credentials; missing secrets fall back to ephemeral test-only values.
/// </summary>
public static class TestHostSecrets
{
    public const string ApiSecretsId = "ebi-alas-v2-backend-local";

    /// <summary>Admin password used by the most recent Apply call (test-only).</summary>
    public static string CurrentAdminPassword { get; private set; } = string.Empty;

    public static string CurrentAdminUserName { get; private set; } = "test-admin";

    public static void Apply(IWebHostBuilder builder, bool useEmptyConnectionStrings = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");
        if (useEmptyConnectionStrings)
        {
            builder.UseSetting("ConnectionStrings:Alas", "");
            builder.UseSetting("ConnectionStrings:WebLoan", "");
        }

        var secrets = new ConfigurationBuilder()
            .AddUserSecrets(typeof(TestHostSecrets).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var signingKey = secrets["Api:Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            signingKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        }

        var adminPassword = secrets["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            adminPassword = $"T-{Guid.NewGuid():N}Aa1";
        }

        CurrentAdminUserName = secrets["Seed:AdminUserName"] ?? "test-admin";
        CurrentAdminPassword = adminPassword;

        builder.UseSetting("Api:Jwt:Issuer", secrets["Api:Jwt:Issuer"] ?? "test-issuer");
        builder.UseSetting("Api:Jwt:Audience", secrets["Api:Jwt:Audience"] ?? "test-clients");
        builder.UseSetting("Api:Jwt:SigningKey", signingKey);

        builder.UseSetting("Seed:AdminUserName", CurrentAdminUserName);
        builder.UseSetting("Seed:AdminPassword", adminPassword);
        builder.UseSetting("Seed:AdminFirstName", secrets["Seed:AdminFirstName"] ?? "Test");
        builder.UseSetting("Seed:AdminLastName", secrets["Seed:AdminLastName"] ?? "Admin");
        builder.UseSetting("Seed:AdminEmail", secrets["Seed:AdminEmail"] ?? "test@example.invalid");
        builder.UseSetting("Seed:AdminBranchId", secrets["Seed:AdminBranchId"] ?? "BR-HQ");
    }
}
