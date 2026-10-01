namespace EBI.ALAS.Api.Features.Auth;
public class CleanupExpiredTokensHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CleanupExpiredTokensHostedService> _logger;
    private readonly TimeSpan _interval;
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    public CleanupExpiredTokensHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<CleanupExpiredTokensHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
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
            var refreshRepo = scope.ServiceProvider
                .GetRequiredService<IRefreshTokenRepository>();
            var revocationRepo = scope.ServiceProvider
                .GetRequiredService<ITokenRevocationRepository>();
            var deletedRefresh = await refreshRepo.CleanupExpiredTokensAsync();
            var deletedRevoked = await revocationRepo.CleanupExpiredTokensAsync();
            _logger.LogInformation(
                "Token cleanup tick: {RefreshDeleted} expired refresh tokens deleted, {RevokedDeleted} expired JTI revocations deleted.",
                deletedRefresh, deletedRevoked);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Token cleanup tick failed; will retry on next interval.");
        }
    }
}
