using System.Text;
using EBI.ALAS.Api.Features.Auth;
using Microsoft.Extensions.Configuration;

namespace EBI.ALAS.Api.Infrastructure.Security;

public static class BankingSecurityValidator
{
    public const int MinSecretKeyLength = 32;
    public const string DefaultPlaceholder = "<JWT_SECRET_KEY_MIN_32_CHARS>";

    public static IServiceCollection AddBankingSecurityHardening(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var logger = loggerFactory.CreateLogger("BankingSecurityValidator");

        var issues = new List<string>();
        var secretKey = configuration["Jwt:SecretKey"];
        var source = ResolveSecretSource(configuration, out var looksLikePlaceholder);

        var length = secretKey?.Length ?? 0;
        if (string.IsNullOrEmpty(secretKey) || length < MinSecretKeyLength)
            issues.Add($"Jwt:SecretKey must be at least {MinSecretKeyLength} characters (found length={length}).");

        if (looksLikePlaceholder)
            issues.Add("Jwt:SecretKey is still the placeholder from appsettings.json.");

        var isProduction = environment.IsProduction();
        if (isProduction && source == SecretSource.AppSettingsJson)
            issues.Add("Jwt:SecretKey is read from appsettings.json in Production. Move to environment variables, Azure Key Vault, or another secrets store.");

        if (issues.Count == 0)
        {
            logger.LogInformation("JWT secret validation passed. Length={Length} chars, Source={Source}", length, source);
            return services;
        }

        var summary = string.Join(" ", issues);

        if (isProduction)
        {
            logger.LogError("Startup aborted: JWT secret validation failed. Length={Length} Source={Source} Issues={Issues}", length, source, summary);
            throw new InvalidOperationException("Startup aborted: JWT secret validation failed in Production. " + summary);
        }

        logger.LogWarning("JWT secret validation found {IssueCount} issue(s) — proceeding because Environment={Environment}. Issues={Issues}", issues.Count, environment.EnvironmentName, summary);
        return services;
    }

    private static SecretSource ResolveSecretSource(IConfiguration configuration, out bool looksLikePlaceholder)
    {
        looksLikePlaceholder = false;
        var secretKey = configuration["Jwt:SecretKey"];
        if (secretKey is not null && (secretKey.Contains(DefaultPlaceholder, StringComparison.Ordinal) || (secretKey.StartsWith("<", StringComparison.Ordinal) && secretKey.EndsWith(">", StringComparison.Ordinal))))
            looksLikePlaceholder = true;

        IConfigurationProvider? winner = null;
        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Reverse())
            {
                if (provider.TryGet("Jwt:SecretKey", out _))
                {
                    winner = provider;
                    break;
                }
            }
        }

        if (winner is null) return SecretSource.None;

        var typeName = winner.GetType().Name;
        return typeName switch
        {
            "EnvironmentVariablesConfigurationProvider" => SecretSource.EnvironmentVariable,
            "AzureKeyVaultConfigurationProvider" => SecretSource.KeyVault,
            "JsonConfigurationProvider" => SecretSource.AppSettingsJson,
            "MemoryConfigurationProvider" => SecretSource.Memory,
            "ChainedConfigurationProvider" => SecretSource.Chained,
            _ => SecretSource.Other
        };
    }

    private enum SecretSource { None, AppSettingsJson, EnvironmentVariable, KeyVault, Memory, Chained, Other }
}