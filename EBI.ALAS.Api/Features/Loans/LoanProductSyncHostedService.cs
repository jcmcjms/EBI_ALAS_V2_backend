namespace EBI.ALAS.Api.Features.Loans;
public class LoanProductSyncHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LoanProductSyncHostedService> _logger;
    private readonly TimeSpan _interval;
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
        }
    }
    private async Task RunOnceSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
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
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LoanProduct sync tick failed; will retry on next interval.");
        }
    }
}
