namespace EBI.ALAS.Api.Features.Auth;

// Hourly cleanup job for the two unbounded-by-design tables that back the
// auth subsystem:
//
//   * RefreshTokens  — one row per login (sliding + absolute expiry).
//   * RevokedTokens  — JTI blacklist; one row per logout / refresh-with-
//                      revocation / change-password.
//
// Without this, both tables grow forever. The unique indexes on TokenHash /
// TokenId fragment over time, the JTI blacklist (which is hit on EVERY
// authenticated request) keeps growing, and storage cost dominates.
//
// EF Core 8 `ExecuteDeleteAsync` translates to a single SQL
// `DELETE FROM … WHERE ExpiresAt < @now [OR AbsoluteExpiry < @now]` bounded
// by the IX_RevokedTokens_ExpiresAt / IX_RefreshTokens_ExpiresAt covering
// indexes declared in AppDbContext.OnModelCreating. No SELECT, no entity
// hydration, no change-tracker pollution.
//
// Lifecycle:
//   * Host calls StartAsync once, ExecuteAsync runs the loop.
//   * PeriodicTimer is cancellation-aware — app shutdown signals the
//     stoppingToken and we exit cleanly.
//   * Each tick gets its own DI scope (the repositories are scoped).
//
// Error policy:
//   * A single failed tick logs and continues — the next tick will
//     retry. A transient DB hiccup shouldn't take the API down for a
//     housekeeping job.
//   * If a tick leaves rows behind (e.g. DB was unreachable for hours),
//     the next tick will pick them up; cleanup is idempotent.
//
// Multi-replica safety:
//   * Multiple replicas running this service concurrently is fine.
//     Both deletes are simple `WHERE ExpiresAt < @now` — no overlap
//     concern, both deletes are idempotent.
public class CleanupExpiredTokensHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CleanupExpiredTokensHostedService> _logger;
    private readonly TimeSpan _interval;

    // Default cadence: 1 hour. The expiries we're cleaning up are
    //   * RefreshTokens sliding expiry:    7 days
    //   * RefreshTokens absolute expiry:  14 days
    //   * RevokedTokens (JTI expiry):      15 min (access-token window)
    // The most active table is RevokedTokens — every logout / refresh
    // adds a row, but each row becomes eligible for delete 15 min later.
    // 1h cleanup means the worst case is 4× the per-hour churn sitting
    // in the table — well within SQL Server's cheap delete range for
    // indexed seeks.
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    // First-tick delay so we don't fight the rest of startup (DB
    // migrations, Redis cold-start, etc.) for the same connection pool
    // in the first few seconds after boot.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    public CleanupExpiredTokensHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<CleanupExpiredTokensHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Override via `TokenCleanup:IntervalMinutes` in appsettings.
        var minutes = configuration.GetValue<int?>("TokenCleanup:IntervalMinutes");
        _interval = minutes is > 0
            ? TimeSpan.FromMinutes(minutes.Value)
            : DefaultInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CleanupExpiredTokensHostedService started. Interval: {IntervalMinutes} minutes. Initial delay: {InitialDelaySeconds}s.",
            _interval.TotalMinutes, InitialDelay.TotalSeconds);

        try
        {
            // Initial delay so DB migrations, distributed cache warmup,
            // and the rest of startup can settle before we hit SQL.
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Run once before the timer so a freshly-booted instance is
        // useful immediately rather than waiting up to 1h for the
        // first tick.
        await RunOnceSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown. PeriodicTimer throws OCE when the
            // stoppingToken is signalled; we swallow it so the
            // BackgroundService returns cleanly.
        }
    }

    private async Task RunOnceSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Each tick gets its own DI scope — IRefreshTokenRepository
            // and ITokenRevocationRepository are scoped (they both
            // resolve AppDbContext). A long-lived host must not capture
            // a scoped service directly, or it'd hold a captive
            // DbContext for the lifetime of the app.
            using var scope = _scopeFactory.CreateScope();

            var refreshRepo = scope.ServiceProvider
                .GetRequiredService<IRefreshTokenRepository>();
            var revocationRepo = scope.ServiceProvider
                .GetRequiredService<ITokenRevocationRepository>();

            var deletedRefresh = await refreshRepo.CleanupExpiredTokensAsync();
            var deletedRevoked = await revocationRepo.CleanupExpiredTokensAsync();

            // Logged as Information (not Debug) so storage-trend
            // dashboards can scrape without enabling Debug. Numbers are
            // the SQL row counts returned by ExecuteDeleteAsync.
            _logger.LogInformation(
                "Token cleanup tick: {RefreshDeleted} expired refresh tokens deleted, {RevokedDeleted} expired JTI revocations deleted.",
                deletedRefresh, deletedRevoked);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // App is shutting down mid-tick. Don't log as an error.
            throw;
        }
        catch (Exception ex)
        {
            // Transient failure (DB outage, network blip, etc.). The
            // next tick will retry. Logged at Warning — Error would
            // page on-call for what's actually a self-healing scenario.
            _logger.LogWarning(ex,
                "Token cleanup tick failed; will retry on next interval.");
        }
    }
}
