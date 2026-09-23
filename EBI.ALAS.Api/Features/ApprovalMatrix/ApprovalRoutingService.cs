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
    string MatchedRule);

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
                    ? DeviationSeverity.Major                       // "Discounted Application Fee"
                    : catalog.GetValueOrDefault(d.ReasonText, DeviationSeverity.Minor);
                if (s > severity) severity = s;
            }
        }

        // TotalExposure is already computed at submission time by LoanMetrics
        var exposure = loan.TotalExposure;
        var loanType = loan.LoanType;

        // Lowest sufficient tier wins (delegation principle).
        var match = authorities
            .Where(a => (loanType == "Renewal" ? a.AllowRenewal : a.AllowNew)
                        && a.MaxSeverity >= severity
                        && a.MaxTotalExposure >= exposure)
            .OrderBy(a => a.Tier)
            .ThenBy(a => a.Priority)
            .FirstOrDefault();

        if (match is null)
            throw new Common.Exceptions.InvalidWorkflowException(loan.Status, "ForApproval",
                $"Total exposure {exposure:N2} / severity {severity} exceeds all delegated authorities; escalate to CreCom (full board).");

        return new RoutingDecision(match.Tier, severity, exposure, loanType,
            $"{match.DisplayName} (Tier {match.Tier}, <= {match.MaxTotalExposure:N0}, {severity})");
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
