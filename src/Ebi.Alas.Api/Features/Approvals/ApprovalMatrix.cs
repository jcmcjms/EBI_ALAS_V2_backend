using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Features.Approvals;

public enum AuthorityLevel
{
    None = 0,
    BranchHead = 1,
    AreaHead = 2,
    RbgProductCredit = 3,
    PresidentCoo = 4,
    CreComBod = 5
}

public sealed record ApprovalInputs(
    LoanType LoanType,
    int MaxDeviationSeverity,
    decimal TotalExposure,
    bool HasRatePriceDeviation);

public sealed record AuthorityDecision(
    AuthorityLevel RequiredLevel,
    string MatchedRule,
    AuthorityLevel? EscalatedFromLevel,
    string? NoAuthorityReason);

public static class ApprovalMatrix
{
    private static readonly string[] RatePriceMarkers =
    [
        "interest rate",
        "rate reduction",
        "price deviation",
        "request of interest rate"
    ];

    public static AuthorityDecision Resolve(ApprovalInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.HasRatePriceDeviation)
        {
            return Blocked("Up to ₱1.5M new/renewal with rate/price deviations — CreCom handling required.");
        }

        if (inputs.TotalExposure > 1_500_000m && inputs.MaxDeviationSeverity > 0)
        {
            return Blocked("Above ₱1.5M with deviations — CreCom handling required.");
        }

        if (inputs.TotalExposure > 1_500_000m)
        {
            return Blocked("Exposure exceeds delegated authority (₱1.5M) — CreCom handling required.");
        }

        var level = ResolveLevel(inputs);
        if (level == AuthorityLevel.CreComBod)
        {
            return Blocked("No officer authority covers this loan — CreCom handling required.");
        }

        return new AuthorityDecision(level, Describe(level), null, null);
    }

    public static bool IsRatePriceDeviation(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var text = description.Trim();
        foreach (var marker in RatePriceMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string Describe(AuthorityLevel level) => level switch
    {
        AuthorityLevel.BranchHead => "Branch Head / OIC",
        AuthorityLevel.AreaHead => "Area Head",
        AuthorityLevel.RbgProductCredit => "RBG / Product / Credit Head",
        AuthorityLevel.PresidentCoo => "President / COO",
        AuthorityLevel.CreComBod => "CreCom / Board",
        _ => "None"
    };

    private static AuthorityLevel ResolveLevel(ApprovalInputs inputs)
    {
        var isReloan = inputs.LoanType == LoanType.Reloan;
        var severity = inputs.MaxDeviationSeverity;

        if (severity >= 2)
        {
            return inputs.TotalExposure <= 1_500_000m
                ? AuthorityLevel.PresidentCoo
                : AuthorityLevel.CreComBod;
        }

        if (severity == 1)
        {
            return inputs.TotalExposure <= 1_000_000m
                ? AuthorityLevel.RbgProductCredit
                : AuthorityLevel.PresidentCoo;
        }

        if (!isReloan)
        {
            return inputs.TotalExposure <= 1_000_000m
                ? AuthorityLevel.RbgProductCredit
                : AuthorityLevel.PresidentCoo;
        }

        if (inputs.TotalExposure <= 300_000m)
        {
            return AuthorityLevel.BranchHead;
        }

        if (inputs.TotalExposure <= 600_000m)
        {
            return AuthorityLevel.AreaHead;
        }

        if (inputs.TotalExposure <= 1_000_000m)
        {
            return AuthorityLevel.RbgProductCredit;
        }

        return AuthorityLevel.PresidentCoo;
    }

    private static AuthorityDecision Blocked(string reason) =>
        new(AuthorityLevel.CreComBod, Describe(AuthorityLevel.CreComBod), null, reason);
}
