using EBI.ALAS.Api.Features.Loans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using EBI.ALAS.Api.Infrastructure.Data;

namespace EBI.ALAS.Api.Features.ApprovalMatrix;

public sealed record RoutingDecision(
    int Tier,
    DeviationSeverity Severity,
    decimal TotalExposure,
    string LoanType,
    string MatchedRule,
    int? MatchedButUnstaffedTier,
    string? NoAuthorityReason);

public interface IApprovalRoutingService
{
    Task<RoutingDecision> RouteAsync(LoanApplication loan, CancellationToken ct = default);
}

public sealed class ApprovalRoutingService : IApprovalRoutingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public ApprovalRoutingService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<RoutingDecision> RouteAsync(LoanApplication loan, CancellationToken ct = default)
    {
        var authorities = await GetAuthoritiesAsync(ct);
        var catalog = await GetCatalogAsync(ct);

        var severity = DeviationSeverity.None;
        if (loan.Deviations is not null)
        {
            foreach (var d in loan.Deviations)
            {
                var s = d.IsFeeOverride
                    ? DeviationSeverity.Major
                    : catalog.GetValueOrDefault(d.ReasonText, DeviationSeverity.Minor);
                if (s > severity) severity = s;
            }
        }

        var exposure = loan.TotalExposure;
        var loanType = loan.LoanType;
        var cycle = loanType == "Renewal" ? LoanCycle.Renewal : LoanCycle.New;

        var inputs = new RoutingInputs(cycle, severity, exposure);
        var match = ApprovalCycleResolver.Match(authorities, inputs);

        if (match is null)
        {
            var reason = exposure > 1_500_000m
                ? $"Exposure exceeds delegated authority (₱{exposure:N2}) — CreCom handling required."
                : $"No authority covers {loanType} loans with {severity} deviation at ₱{exposure:N2} exposure.";
            return new RoutingDecision(0, severity, exposure, loanType,
                MatchedRule: "No matching authority",
                MatchedButUnstaffedTier: null,
                NoAuthorityReason: reason);
        }

        return new RoutingDecision(match.Tier, severity, exposure, loanType,
            MatchedRule: $"{match.DisplayName} (Tier {match.Tier}, ≤ {match.MaxTotalExposure:N0}, {severity})",
            MatchedButUnstaffedTier: null,
            NoAuthorityReason: null);
    }

    private Task<List<ApprovalAuthority>> GetAuthoritiesAsync(CancellationToken ct) =>
        _cache.GetOrCreateAsync("approval-matrix:authorities", async e =>
        {
            e.Size = 1;
            e.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await _db.ApprovalAuthorities.AsNoTracking()
                .OrderBy(a => a.Tier).ThenBy(a => a.Priority)
                .ToListAsync(ct);
        })!;

    private Task<Dictionary<string, DeviationSeverity>> GetCatalogAsync(CancellationToken ct) =>
        _cache.GetOrCreateAsync("approval-matrix:catalog", async e =>
        {
            e.Size = 1;
            e.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await _db.DeviationCatalog.AsNoTracking()
                .ToDictionaryAsync(x => x.Description, x => x.Severity, ct);
        })!;
}