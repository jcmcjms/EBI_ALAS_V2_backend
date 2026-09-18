using System.Text;
using EBI.ALAS.Api.Features.Auth;
using Microsoft.Extensions.Configuration;

namespace EBI.ALAS.Api.Infrastructure.Security;

public static class BankingSecurityValidator
{
    public const int MinSecretKeyLength = 32;
    // Banking-grade: require 64+ characters for sufficient entropy
    public const int BankingGradeMinLength = 64;
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

        // Entropy check — a 64-char key with low entropy (e.g., "aaaa...") is
        // no better than a short key. Check character diversity.
        if (!string.IsNullOrEmpty(secretKey) && length >= MinSecretKeyLength && IsLowEntropy(secretKey))
            issues.Add("Jwt:SecretKey has low entropy. Use a cryptographically random string with diverse characters.");

        // Banking-grade length warning (non-fatal in dev, fatal in prod)
        if (!string.IsNullOrEmpty(secretKey) && length < BankingGradeMinLength && length >= MinSecretKeyLength)
        {
            if (environment.IsProduction())
                issues.Add($"Jwt:SecretKey should be at least {BankingGradeMinLength} characters for banking-grade security (found {length}).");
            else
                logger.LogWarning("Jwt:SecretKey is {Length} chars. Banking-grade recommendation: {Recommended}+ chars.", length, BankingGradeMinLength);
        }

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

    /// <summary>
    /// Checks if a secret key has low entropy (too many repeated characters,
    /// sequential patterns, or keyboard walks). A high-entropy key should
    /// have a diverse character distribution.
    /// </summary>
    private static bool IsLowEntropy(string key)
    {
        // Count unique characters — if less than 25% of length, it's low entropy
        var uniqueChars = key.Distinct().Count();
        var uniqueRatio = (double)uniqueChars / key.Length;

        if (uniqueRatio < 0.25)
            return true;

        // Check for sequential patterns (e.g., "abcdefgh", "12345678")
        var sequentialCount = 0;
        for (int i = 0; i < key.Length - 2; i++)
        {
            if (key[i + 1] == key[i] + 1 && key[i + 2] == key[i] + 2)
                sequentialCount++;
        }

        // If more than 10% of the key is sequential patterns, it's low entropy
        if (sequentialCount > key.Length / 10)
            return true;

        // Check for repeated character runs (e.g., "aaaa", "1111")
        var maxRun = 1;
        var currentRun = 1;
        for (int i = 1; i < key.Length; i++)
        {
            if (key[i] == key[i - 1])
            {
                currentRun++;
                maxRun = Math.Max(maxRun, currentRun);
            }
            else
            {
                currentRun = 1;
            }
        }

        // Any run of 4+ identical characters indicates low entropy
        if (maxRun >= 4)
            return true;

        return false;
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