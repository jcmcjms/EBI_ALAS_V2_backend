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

    private static SubmitLoanRequest Request(string branchId) => new(
        "G-1",
        "Client",
        branchId,
        "A16",
        "A16 - Salary Loan",
        LoanType.New,
        1000m,
        30,
        0.05m);

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
        TimeProvider.System);

    [Fact]
    public async Task SubmitLoan_NonAdmin_UsesCallerBranch_IgnoresRequestBranch()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        var result = await handler.HandleAsync(Request("BR-HACK"), caller, CancellationToken.None);

        Assert.Equal("BR-A", result.BranchId);
    }

    [Fact]
    public async Task SubmitLoan_Admin_MayUseRequestBranch()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-HQ");

        var result = await handler.HandleAsync(Request("BR-OTHER"), caller, CancellationToken.None);

        Assert.Equal("BR-OTHER", result.BranchId);
    }

    [Fact]
    public async Task SubmitLoan_AdminWithoutRequestBranch_UsesCallerBranch()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-HQ");

        var result = await handler.HandleAsync(Request("  "), caller, CancellationToken.None);

        Assert.Equal("BR-HQ", result.BranchId);
    }

    [Fact]
    public async Task SubmitLoan_WhenRecommendationRequired_StartsAtRecommendation()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        var result = await handler.HandleAsync(Request("BR-A"), caller, CancellationToken.None);

        Assert.Equal(nameof(LoanStatus.ForRecommendation), result.Status);
        var queueItem = await db.WorkflowQueueItems.SingleAsync();
        Assert.Equal(WorkflowStage.Recommendation, queueItem.Stage);
    }

    [Fact]
    public async Task SubmitLoan_WhenRecommendationSkipped_StartsAtEvaluation()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: false);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        var result = await handler.HandleAsync(Request("BR-A"), caller, CancellationToken.None);

        Assert.Equal(nameof(LoanStatus.ForChecking), result.Status);
        var queueItem = await db.WorkflowQueueItems.SingleAsync();
        Assert.Equal(WorkflowStage.Evaluation, queueItem.Stage);
    }

    [Fact]
    public async Task SubmitLoan_AllocatesReferenceStyleLamAndGroupNumbers()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var request = new SubmitLoanRequest(
            null,
            "Client",
            "BR-A",
            "A16",
            "A16 - Salary Loan",
            LoanType.New,
            1000m,
            30,
            0.05m);

        var result = await handler.HandleAsync(request, caller, CancellationToken.None);

        Assert.Matches(@"^LAM-\d{8}-\d{6}$", result.LamId);
        Assert.Matches(@"^APP-\d{8}-\d{6}$", result.ApplicationGroupNo);
        Assert.Equal("A16", result.ProductCode);
        Assert.Equal("A16 - Salary Loan", result.Product);
    }

    [Fact]
    public async Task SubmitLoan_SequentialLamIdsIncrement()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        var first = await handler.HandleAsync(Request("BR-A"), caller, CancellationToken.None);
        var second = await handler.HandleAsync(Request("BR-A"), caller, CancellationToken.None);

        Assert.NotEqual(first.LamId, second.LamId);
        Assert.True(string.CompareOrdinal(second.LamId, first.LamId) > 0);
    }

    [Fact]
    public async Task SubmitLoan_UsesProvidedGroupNo()
    {
        await using var db = Db();
        var handler = Handler(db, requireRecommendation: true);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");
        var request = new SubmitLoanRequest(
            "APP-20261013-000042",
            "Client",
            "BR-A",
            "A16",
            "A16 - Salary Loan",
            LoanType.New,
            1000m,
            30,
            0.05m);

        var result = await handler.HandleAsync(request, caller, CancellationToken.None);

        Assert.Equal("APP-20261013-000042", result.ApplicationGroupNo);
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
