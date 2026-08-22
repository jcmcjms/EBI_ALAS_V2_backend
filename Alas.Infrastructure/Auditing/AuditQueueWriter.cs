using System.ComponentModel;
using Alas.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alas.Infrastructure.Auditing;

public sealed class AuditQueueWriter: BackgroundWorker
{
    private const int BatchSize = 100;
    private readonly AuditChannel _channel;
    private readonly IServiceScopeFactory _factory;
    private readonly ILogger<AuditQueueWriter> _logger;

    public AuditQueueWriter(
        AuditChannel channel,
        IServiceScopeFactory factory,
        ILogger<AuditQueueWriter> logger)
    {
        _channel = channel;
        _factory = factory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditLog>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                while (batch.Count < BatchSize &&
                       await _channel.Reader.WaitToReadAsync(stoppingToken))
                {
                    while (batch.Count < BatchSize &&
                           _channel.Reader.TryRead(out var auditLog))
                    {
                        batch.Add(auditLog);
                    }
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                using var scope = _factory.CreateScope();

                var dbContext = scope.ServiceProvider.GetRequiredService<AlasDbContext>();
                
                dbContext.Aud
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to write audit logs.");
        }
    }
}