using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Features.Approvals;

public enum AuthorityLevel
{
    None = 0,
    BranchHead = 1,
    OfficerInCharge = 2,
    AreaHead = 3,
    RbgHead = 4,
    ProductHead = 5,
    CreditHead = 6,
    Coo = 7,
    PresidentCeo = 8,
    CreComBod = 9
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

        // Level B: above ₱1.5M exceeds every delegated officer ceiling.
        // Rate/price deviations are also CreCom only in this band (not below).
        if (inputs.TotalExposure > 1_500_000m)
        {
            return Blocked("The loan exceeds the ₱1.5M delegated limit, so CreCom must approve it.");
        }

        // Rate/price items (e.g. Interest rate reduction) are Major in the
        // amended deviation list; below ₱1.5M they use the major ladder.
        var severity = inputs.HasRatePriceDeviation
            ? Math.Max(inputs.MaxDeviationSeverity, 2)
            : inputs.MaxDeviationSeverity;

        var level = ResolveLevel(inputs.LoanType, severity, inputs.TotalExposure);
        if (level == AuthorityLevel.CreComBod)
        {
            return Blocked("No officer tier covers this loan, so CreCom must approve it.");
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
        AuthorityLevel.BranchHead => "Branch Head",
        AuthorityLevel.OfficerInCharge => "Officer In Charge",
        AuthorityLevel.AreaHead => "Area Head",
        AuthorityLevel.RbgHead => "RBG Head",
        AuthorityLevel.ProductHead => "Product Head",
        AuthorityLevel.CreditHead => "Credit Head",
        AuthorityLevel.Coo => "COO",
        AuthorityLevel.PresidentCeo => "President / CEO",
        AuthorityLevel.CreComBod => "CreCom / Board",
        _ => "None"
    };

    private static AuthorityLevel ResolveLevel(LoanType loanType, int severity, decimal totalExposure)
    {
        var isReloan = loanType == LoanType.Reloan;

        // Level E / D: major deviations → COO ≤ ₱1.2M, President ≤ ₱1.5M.
        if (severity >= 2)
        {
            return totalExposure <= 1_200_000m
                ? AuthorityLevel.Coo
                : AuthorityLevel.PresidentCeo;
        }

        // Minor deviations → RBG/Product/Credit ≤ ₱1M, else President or COO.
        if (severity == 1)
        {
            return totalExposure <= 1_000_000m
                ? AuthorityLevel.RbgHead
                : AuthorityLevel.Coo;
        }

        if (!isReloan)
        {
            return totalExposure <= 1_000_000m
                ? AuthorityLevel.RbgHead
                : AuthorityLevel.Coo;
        }

        // Reloan, no deviation: Branch ≤ ₱300K, Area ≤ ₱600K, RBG ≤ ₱1M.
        if (totalExposure <= 300_000m)
        {
            return AuthorityLevel.BranchHead;
        }

        if (totalExposure <= 600_000m)
        {
            return AuthorityLevel.AreaHead;
        }

        if (totalExposure <= 1_000_000m)
        {
            return AuthorityLevel.RbgHead;
        }

        if (totalExposure <= 1_200_000m)
        {
            return AuthorityLevel.Coo;
        }

        return AuthorityLevel.PresidentCeo;
    }

    private static AuthorityDecision Blocked(string reason) =>
        new(AuthorityLevel.CreComBod, Describe(AuthorityLevel.CreComBod), null, reason);
}
