using System.Text;
using EBI.ALAS.Api.Features.Auth;
using Microsoft.Extensions.Configuration;

namespace EBI.ALAS.Api.Infrastructure.Security;

/// <summary>
/// Fail-fast validation of security-critical configuration. Designed to be
/// invoked at startup so that a misconfigured production deployment never
/// silently boots with a weak JWT signing key.
/// </summary>
public static class BankingSecurityValidator
{
    /// <summary>
    /// Minimum acceptable length of the JWT signing key, in characters.
    /// 32 chars ≈ 256 bits, the recommended floor for HS256.
    /// </summary>
    public const int MinSecretKeyLength = 32;

    /// <summary>
    /// The placeholder text shipped in <c>appsettings.json</c>. Any key equal
    /// to (or contained within) this value is considered un-configured.
    /// </summary>
    public const string DefaultPlaceholder = "<JWT_SECRET_KEY_MIN_32_CHARS>";

    /// <summary>
    /// Register and execute the security hardening checks. Must be called
    /// BEFORE <c>builder.Build()</c>.
    /// </summary>
    /// <param name="services">Service collection (returned for chaining — no services are registered here).</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Web host environment, used to pick fail-fast vs warn-only behaviour.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown in non-Development environments when the JWT secret key is
    /// missing, too short, or sourced from <c>appsettings.json</c>.
    /// </exception>
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

        // ─── Length check ────────────────────────────────────────────────
        var length = secretKey?.Length ?? 0;
        if (string.IsNullOrEmpty(secretKey) || length < MinSecretKeyLength)
        {
            issues.Add($"Jwt:SecretKey must be at least {MinSecretKeyLength} characters (found length={length}).");
        }

        // ─── Placeholder check ──────────────────────────────────────────
        if (looksLikePlaceholder)
        {
            issues.Add("Jwt:SecretKey is still the placeholder from appsettings.json.");
        }

        // ─── Source check (production only) ─────────────────────────────
        // In Production we forbid reading the secret from appsettings.json
        // entirely — it must come from env var / Key Vault / secrets store.
        var isProduction = environment.IsProduction();
        if (isProduction && source == SecretSource.AppSettingsJson)
        {
            issues.Add("Jwt:SecretKey is read from appsettings.json in Production. " +
                       "Move to environment variables, Azure Key Vault, or another secrets store.");
        }

        // ─── Report ─────────────────────────────────────────────────────
        if (issues.Count == 0)
        {
            logger.LogInformation(
                "JWT secret validation passed. Length={Length} chars, Source={Source}",
                length, source);
            return services;
        }

        var summary = string.Join(" ", issues);

        if (isProduction)
        {
            // Fail fast. Do NOT include the secret value.
            logger.LogError(
                "Startup aborted: JWT secret validation failed. Length={Length} Source={Source} Issues={Issues}",
                length, source, summary);
            throw new InvalidOperationException(
                "Startup aborted: JWT secret validation failed in Production. " + summary);
        }

        // Development: warn loudly but allow boot so engineers can keep working.
        logger.LogWarning(
            "JWT secret validation found {IssueCount} issue(s) — proceeding because Environment={Environment}. Issues={Issues}",
            issues.Count, environment.EnvironmentName, summary);

        return services;
    }

    /// <summary>
    /// Walk the configuration provider chain to determine where the
    /// <c>Jwt:SecretKey</c> value was loaded from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ASP.NET Core default configuration chain (appsettings.json →
    /// appsettings.{Env}.json → user-secrets → environment variables →
    /// command-line) is ordered by precedence: providers registered LAST win.
    /// </para>
    /// <para>
    /// To classify the source we look at the highest-precedence provider
    /// that contains the key. We match on the type name because some of the
    /// concrete provider types live in packages we don't take a hard
    /// reference on (e.g. AzureKeyVault) and we want to remain trim-safe.
    /// </para>
    /// </remarks>
    private static SecretSource ResolveSecretSource(
        IConfiguration configuration,
        out bool looksLikePlaceholder)
    {
        looksLikePlaceholder = false;

        var secretKey = configuration["Jwt:SecretKey"];
        if (secretKey is not null
            && (secretKey.Contains(DefaultPlaceholder, StringComparison.Ordinal)
                || (secretKey.StartsWith("<", StringComparison.Ordinal)
                    && secretKey.EndsWith(">", StringComparison.Ordinal))))
        {
            looksLikePlaceholder = true;
        }

        // Find the highest-precedence provider that actually has the key.
        // `Providers` is enumerated in registration order, so we reverse it.
        // Only `IConfigurationRoot` exposes `Providers`; for plain
        // `IConfiguration` we cannot introspect source — treat as Unknown.
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

        if (winner is null)
        {
            return SecretSource.None;
        }

        // Match by type name (case-insensitive suffix match). This is robust
        // against namespace moves between .NET versions.
        var typeName = winner.GetType().Name;
        return typeName switch
        {
            "EnvironmentVariablesConfigurationProvider" => SecretSource.EnvironmentVariable,
            "AzureKeyVaultConfigurationProvider"       => SecretSource.KeyVault,
            "JsonConfigurationProvider"                => SecretSource.AppSettingsJson,
            "MemoryConfigurationProvider"              => SecretSource.Memory,
            "ChainedConfigurationProvider"             => SecretSource.Chained,
            _ => SecretSource.Other
        };
    }

    private enum SecretSource
    {
        None,
        AppSettingsJson,
        EnvironmentVariable,
        KeyVault,
        Memory,
        Chained,
        Other
    }
}