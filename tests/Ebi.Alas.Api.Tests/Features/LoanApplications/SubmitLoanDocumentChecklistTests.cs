using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.Submit;
using Ebi.Alas.Api.Features.LoanProducts;
using Ebi.Alas.Api.Features.SystemSettings;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class SubmitLoanDocumentChecklistTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"submit-req-{Guid.NewGuid():N}")
            .Options);

    private static SubmitLoanHandler Handler(AlasDbContext db) => new(
        db,
        new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
        new WorkflowQueueService(db, TimeProvider.System),
        new WorkflowConfiguration(
            Options.Create(new WorkflowOptions()).ToMonitor(),
            new FakeSettingsStore(new SettingSnapshot(true, DateTime.UtcNow, "Admin"))),
        new ApplicationNumberGenerator(db),
        new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
        TimeProvider.System);

    [Fact]
    public async Task Submit_CreatesChecklistItems_FromProductRequirements()
    {
        await using var db = Db();
        db.LoanProductDocumentRequirements.Add(
            LoanProductDocumentRequirement.Create("A16", "A1004", "A1004"));
        db.LoanProductDocumentRequirements.Add(
            LoanProductDocumentRequirement.Create("A16", "PIC02", "Photo ID"));
        await db.SaveChangesAsync();

        var handler = Handler(db);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var request = new SubmitLoanApplicationsRequest(
            "G-1",
            "Client",
            "BR-A",
            LoanType.New,
            [new SubmitLoanLine("A16", "A16 - Salary", 1000m, 30, 0.05m)]);

        await handler.HandleAsync(request, caller, CancellationToken.None);

        var loanId = db.LoanApplications.Single().Id;
        var items = await db.DocumentChecklistItems
            .Where(i => i.LoanApplicationId == loanId)
            .ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Code == "A1004" && i.Name == "A1004");
        Assert.Contains(items, i => i.Code == "PIC02" && i.Name == "Photo ID");
    }

    [Fact]
    public async Task Submit_WithoutRequirements_CreatesNoChecklistItems()
    {
        await using var db = Db();
        var handler = Handler(db);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var request = new SubmitLoanApplicationsRequest(
            "G-1",
            "Client",
            "BR-A",
            LoanType.New,
            [new SubmitLoanLine("A16", "A16 - Salary", 1000m, 30, 0.05m)]);

        await handler.HandleAsync(request, caller, CancellationToken.None);

        Assert.Empty(db.DocumentChecklistItems);
    }

    private sealed class FakeSettingsStore(SettingSnapshot snapshot) : ISystemSettingsStore
    {
        public Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default)
            => Task.FromResult(new SettingSnapshot(value, DateTime.UtcNow, "Admin"));
    }
}
