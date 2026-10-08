using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Infrastructure.Background;

public sealed class DisbursementSyncHostedService(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<DisbursementSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AlasDbContext>();
                var webLoans = scope.ServiceProvider.GetRequiredService<IWebLoanReader>();
                var pending = await db.LoanApplications
                    .Where(l => l.Status == LoanStatus.ForDisbursement || l.Status == LoanStatus.Disbursed)
                    .OrderBy(l => l.CreatedAt)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                foreach (var loan in pending)
                {
                    var outstanding = await webLoans.GetOutstandingAsync(loan.ClientName, 5, stoppingToken);
                    var disbursed = outstanding.Any(o => string.Equals(o.Status, "Active", StringComparison.OrdinalIgnoreCase));
                    if (disbursed && loan.Status == LoanStatus.ForDisbursement)
                    {
                        loan.TransitionTo(LoanStatus.Disbursed, timeProvider.GetUtcNow());
                    }
                    else if (disbursed && loan.Status == LoanStatus.Disbursed)
                    {
                        loan.TransitionTo(LoanStatus.OnGoing, timeProvider.GetUtcNow());
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Disbursement sync failed");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
