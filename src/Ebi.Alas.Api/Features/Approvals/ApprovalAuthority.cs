using System.Text;

namespace Ebi.Alas.Api.Features.Approvals;

public static class AuthorityScopes
{
    public const int Branch = 0;
    public const int Area = 1;
    public const int Global = 2;
}

public sealed class ApprovalAuthority
{
    private ApprovalAuthority()
    {
        Key = string.Empty;
        DisplayName = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Key { get; private set; }

    public string DisplayName { get; private set; }

    public int Tier { get; private set; }

    public int Priority { get; private set; }

    public bool AllowNew { get; private set; }

    public bool AllowRenewal { get; private set; }

    public int MaxSeverity { get; private set; }

    public decimal MaxTotalExposure { get; private set; }

    public int ScopeType { get; private set; }

    public static ApprovalAuthority Create(
        string displayName,
        int tier,
        int priority,
        bool allowNew,
        bool allowRenewal,
        int maxSeverity,
        decimal maxTotalExposure,
        int scopeType)
    {
        Validate(displayName, tier, priority, maxSeverity, maxTotalExposure, scopeType);

        return new ApprovalAuthority
        {
            Id = Guid.NewGuid(),
            Key = Slugify(displayName),
            DisplayName = displayName.Trim(),
            Tier = tier,
            Priority = priority,
            AllowNew = allowNew,
            AllowRenewal = allowRenewal,
            MaxSeverity = maxSeverity,
            MaxTotalExposure = maxTotalExposure,
            ScopeType = scopeType,
        };
    }

    public void Update(
        string displayName,
        int tier,
        int priority,
        bool allowNew,
        bool allowRenewal,
        int maxSeverity,
        decimal maxTotalExposure,
        int scopeType)
    {
        Validate(displayName, tier, priority, maxSeverity, maxTotalExposure, scopeType);

        DisplayName = displayName.Trim();
        Tier = tier;
        Priority = priority;
        AllowNew = allowNew;
        AllowRenewal = allowRenewal;
        MaxSeverity = maxSeverity;
        MaxTotalExposure = maxTotalExposure;
        ScopeType = scopeType;
    }

    private static void Validate(
        string displayName,
        int tier,
        int priority,
        int maxSeverity,
        decimal maxTotalExposure,
        int scopeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (displayName.Trim().Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(displayName), "Display name must be at most 100 characters.");
        }

        if (tier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), "Tier must be at least 1.");
        }

        if (priority < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priority), "Priority must be 0 or greater.");
        }

        if (maxSeverity is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSeverity), "Max severity must be between 0 and 2.");
        }

        if (maxTotalExposure < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalExposure), "Max exposure must be 0 or greater.");
        }

        if (scopeType is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(scopeType), "Scope must be between 0 and 2.");
        }
    }

    private static string Slugify(string displayName)
    {
        var builder = new StringBuilder();
        var dash = false;
        foreach (var c in displayName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                dash = false;
            }
            else if (!dash)
            {
                builder.Append('-');
                dash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}

public sealed class ApprovalAuthorityUser
{
    public Guid ApprovalAuthorityId { get; set; }

    public Guid UserId { get; set; }
}
