using Ebi.Alas.Api.Features.Users.Seed;

namespace Ebi.Alas.Api.Infrastructure.Background;

public sealed class AdminSeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AdminSeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<AdminUserSeeder>();
        var created = await seeder.SeedAsync(cancellationToken);
        if (created)
        {
            logger.LogInformation("Admin seed user created from user-secrets.");
        }
        else
        {
            logger.LogInformation("Admin seed user already present; existing credentials left unchanged.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
