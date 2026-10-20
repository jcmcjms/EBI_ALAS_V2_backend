using System.Text.Json;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.Submit;
using Ebi.Alas.Api.Features.SystemSettings;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class SubmitLoanBranchTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"submit-branch-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static SubmitLoanApplicationsRequest Request(string branchId, params SubmitLoanLine[] loans) => new(
        "G-1",
        "Client",
        branchId,
        LoanType.New,
        loans.Length == 0
            ? [new SubmitLoanLine("A16", "A16 - Salary Loan", 1000m, 30, 0.05m)]
            : loans);

    private static IWorkflowConfiguration Config(bool requireRecommendation)
    {
        var store = new FakeSettingsStore(
            requireRecommendation
                ? new SettingSnapshot(true, DateTime.UtcNow, "Admin")
                : new SettingSnapshot(false, DateTime.UtcNow, "Admin"));
        return new WorkflowConfiguration(
            Options.Create(new WorkflowOptions()).ToMonitor(),
            store);
    }

    private static SubmitLoanHandler Handler(AlasDbContext db, bool requireRecommendation) => new(
        db,
        new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
        new WorkflowQueueService(db, TimeProvider.System),
        Config(requireRecommendation),
        new ApplicationNumberGenerator(db),
        new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
        realtime: null,
        TimeProvider.System);

    [Fact]
    public async Task SubmitLoan_NonAdmin_UsesCallerBranch_IgnoresRequestBranch()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        var result = await handler.HandleAsync(Request("BR-HACK"), caller, CancellationToken.None);

        Assert.Equal("BR-A", db.LoanApplications.Single().BranchId);
        Assert.Equal("G-1", result.ApplicationGroupNo);
    }

    [Fact]
    public async Task SubmitLoan_MultipleLoans_ShareGroupAndGetSequentialLamIds()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var request = Request(
            "BR-A",
            new SubmitLoanLine("A16", "A16 - Salary", 1000m, 30, 0.05m),
            new SubmitLoanLine("C02", "C02 - Payday", 2000m, 15, 0.08m));

        var result = await handler.HandleAsync(request, caller, CancellationToken.None);

        Assert.Equal(2, result.Loans.Count);
        Assert.Equal(result.ApplicationGroupNo, result.Loans[0].Id == result.Loans[1].Id
            ? result.ApplicationGroupNo
            : result.ApplicationGroupNo);
        Assert.All(result.Loans, l => Assert.Matches(@"^LAM-\d{8}-\d{6}$", l.LamId));
        Assert.NotEqual(result.Loans[0].LamId, result.Loans[1].LamId);
        Assert.Equal(2, await db.LoanApplications.CountAsync());
        Assert.Equal(result.ApplicationGroupNo, await db.LoanApplications.Select(l => l.ApplicationGroupNo).FirstAsync());
        Assert.Equal(2, await db.WorkflowQueueItems.CountAsync());
        Assert.Equal("A16", result.Loans[0].ProductCode);
        Assert.Equal("C02", result.Loans[1].ProductCode);
    }

    [Fact]
    public async Task SubmitLoan_MultiLoanForm_ScopesObligationsToEachApplication()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        using var formDoc = JsonDocument.Parse(
            $$"""
            {
              "client": { "cisId": "C-1" },
              "loans": [
                {
                  "loanNo": "PN-1",
                  "parameters": { "nthpDate": "{{today}}" },
                  "ebiReloans": [
                    {
                      "pn": "OLD-1",
                      "name": "First only",
                      "existingDeduction": 100,
                      "outstandingBalance": 1000,
                      "payToClose": 1000
                    }
                  ]
                },
                { "loanNo": "PN-2", "parameters": { "nthpDate": "{{today}}" }, "ebiReloans": [] }
              ],
              "outstandingLoans": []
            }
            """);
        var request = new SubmitLoanApplicationsRequest(
            "G-1",
            "Client",
            "BR-A",
            LoanType.New,
            [
                new SubmitLoanLine("A16", "A16 - Salary", 1000m, 30, 0.05m),
                new SubmitLoanLine("C02", "C02 - Payday", 2000m, 15, 0.08m),
            ],
            formDoc.RootElement.Clone());

        await handler.HandleAsync(request, caller, CancellationToken.None);

        var first = await db.LoanApplications.SingleAsync(l => l.ProductCode == "A16");
        var second = await db.LoanApplications.SingleAsync(l => l.ProductCode == "C02");

        using var firstForm = JsonDocument.Parse(first.FormJson!);
        using var secondForm = JsonDocument.Parse(second.FormJson!);

        var firstLoans = firstForm.RootElement.GetProperty("loans");
        Assert.Equal(1, firstLoans.GetArrayLength());
        Assert.Equal("PN-1", firstLoans[0].GetProperty("loanNo").GetString());
        Assert.Equal(1, firstLoans[0].GetProperty("ebiReloans").GetArrayLength());
        Assert.Equal("OLD-1", firstLoans[0].GetProperty("ebiReloans")[0].GetProperty("pn").GetString());

        var secondLoans = secondForm.RootElement.GetProperty("loans");
        Assert.Equal(1, secondLoans.GetArrayLength());
        Assert.Equal("PN-2", secondLoans[0].GetProperty("loanNo").GetString());
        Assert.Equal(0, secondLoans[0].GetProperty("ebiReloans").GetArrayLength());
    }

    [Fact]
    public async Task SubmitLoan_BlankGroupNo_AllocatesAppGroupNumber()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var request = new SubmitLoanApplicationsRequest(
            null,
            "Client",
            "BR-A",
            LoanType.New,
            [new SubmitLoanLine("A16", "A16 - Salary", 1000m, 30, 0.05m)]);

        var result = await handler.HandleAsync(request, caller, CancellationToken.None);

        Assert.Matches(@"^APP-\d{8}-\d{6}$", result.ApplicationGroupNo);
        Assert.Matches(@"^LAM-\d{8}-\d{6}$", result.Loans[0].LamId);
    }

    [Fact]
    public async Task SubmitLoan_EmptyLoans_Throws()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var request = new SubmitLoanApplicationsRequest(
            "G-1", "Client", "BR-A", LoanType.New, []);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(request, caller, CancellationToken.None));
    }

    [Fact]
    public async Task SubmitLoan_WhenRecommendationSkipped_StartsAtEvaluation()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: false);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        var result = await handler.HandleAsync(Request("BR-A"), caller, CancellationToken.None);

        Assert.Equal(nameof(LoanStatus.ForChecking), result.Loans[0].Status);
        var queueItem = await db.WorkflowQueueItems.SingleAsync();
        Assert.Equal(WorkflowStage.Evaluation, queueItem.Stage);
    }

    private sealed class FakeSettingsStore(SettingSnapshot snapshot) : ISystemSettingsStore
    {
        public Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default)
            => Task.FromResult(new SettingSnapshot(value, DateTime.UtcNow, "Admin"));
    }
}

internal static class OptionsMonitorExtensions
{
    public static IOptionsMonitor<T> ToMonitor<T>(this IOptions<T> options)
        where T : class, new()
        => new StaticOptionsMonitor<T>(options.Value);

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
