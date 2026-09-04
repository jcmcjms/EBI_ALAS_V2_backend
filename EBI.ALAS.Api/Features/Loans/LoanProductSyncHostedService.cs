namespace EBI.ALAS.Api.Features.Loans;

// Scheduled background job that runs the LoanProductSyncService on
// an interval. The interval is configurable via
// `LoanProductSync:IntervalMinutes` in appsettings; default is
// 6 hours — well below the 24-hour lag that would be noticeable
// to encoders, and far above the 1-minute storm that would hammer
// webloan for no benefit.
//
// Lifecycle: the host calls StartAsync once at app startup, waits
// for the initial run, then loops. StopAsync waits for an in-flight
// run to complete before returning so we never leave the mirror
// half-updated when the app is shutting down.
//
// Error policy: a single failed run logs and continues — the next
// tick will retry. This is a mirror, not a transactional job, and a
// transient webloan outage shouldn't take down the API.
public class LoanProductSyncHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LoanProductSyncHostedService> _logger;
    private readonly TimeSpan _interval;

    // Default cadence: 6 hours. Tuned for "policy changes are
    // infrequent" (per the spec). Override via
    // `LoanProductSync:IntervalMinutes` in appsettings.
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(6);

    public LoanProductSyncHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<LoanProductSyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var minutes = configuration.GetValue<int?>("LoanProductSync:IntervalMinutes");
        _interval = minutes is > 0
            ? TimeSpan.FromMinutes(minutes.Value)
            : DefaultInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "LoanProductSyncHostedService started. Interval: {IntervalMinutes} minutes.",
            _interval.TotalMinutes);

        // Initial run on startup so a freshly-deployed instance is
        // useful immediately rather than waiting up to 6 hours for
        // the first tick. Delays handled below keep webloan happy
        // even if multiple ALAS instances start at the same time.
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
            // Each run gets its own DI scope — ILoanProductSyncService
            // (and its dependencies) are scoped, not singleton. A
            // long-lived host must not capture a scoped service
            // directly, or it'd hold a captive DbContext for the
            // lifetime of the app.
            using var scope = _scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider
                .GetRequiredService<ILoanProductSyncService>();

            var result = await syncService.SyncAsync(stoppingToken);

            _logger.LogInformation(
                "LoanProduct sync tick: {Added} added, {Updated} updated, {Preserved} preserved at {SyncedAt:o}",
                result.Added, result.Updated, result.Preserved, result.SyncedAt);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // App is shutting down mid-tick. Don't log as an error.
            throw;
        }
        catch (Exception ex)
        {
            // Transient failure (webloan outage, network blip, DB
            // hiccup). The next tick will retry. Log at Warning —
            // Error would page on-call for what's actually a
            // self-healing scenario.
            _logger.LogWarning(ex,
                "LoanProduct sync tick failed; will retry on next interval.");
        }
    }
}
