using System.Text.Json;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.Submit;
using Ebi.Alas.Api.Features.SystemSettings;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using SignatureTestImages = Ebi.Alas.Api.Tests.Features.Users.SignatureTestImages;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

/// <summary>
/// Submit must fail before any loan row is written when the encoder has no
/// e-signature. Otherwise the rejected first attempt still persists, and the
/// retry after uploading a signature creates a second application.
/// </summary>
public sealed class SubmitLoanSignatureGateTests
{
    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"submit-sig-{Guid.NewGuid():N}")
            .Options);

    private static SubmitLoanApplicationsRequest Request() => new(
        "G-1",
        "Client",
        "BR-A",
        LoanType.New,
        [new SubmitLoanLine("A16", "A16 - Salary Loan", 1000m, 30, 0.05m)],
        JsonDocument.Parse(
            """{"client":{"cisId":"0880530879","address":"123 Rizal St"},"loans":[]}""").RootElement.Clone());

    private static SubmitLoanHandler Handler(AlasDbContext db) => new(
        db,
        new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
        new WorkflowQueueService(db, TimeProvider.System),
        new WorkflowConfiguration(
            Options.Create(new WorkflowOptions()).ToMonitor(),
            new FakeSettingsStore(new SettingSnapshot(false, DateTime.UtcNow, "Admin"))),
        new ApplicationNumberGenerator(db),
        new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
        new EmptyChecklistReader(),
        new FakeWebLoanReader(),
        realtime: null,
        TimeProvider.System);

    [Fact]
    public async Task Submit_WithoutEncoderSignature_ThrowsAndPersistsNoLoans()
    {
        await using var db = Db();
        var handler = Handler(db);
        var caller = await TestUsers.SeedUnsignedCallerAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(Request(), caller, CancellationToken.None));

        Assert.Empty(await db.LoanApplications.ToListAsync());
    }

    [Fact]
    public async Task Submit_WithEncoderSignature_PersistsExactlyOnce()
    {
        await using var db = Db();
        var handler = Handler(db);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(Request(), caller, CancellationToken.None);

        Assert.Single(result.Loans);
        Assert.Single(await db.LoanApplications.ToListAsync());
    }

    [Fact]
    public async Task Submit_RetryAfterSignatureUpload_CreatesOnlyTheRetryApplication()
    {
        await using var db = Db();
        var handler = Handler(db);
        var caller = await TestUsers.SeedUnsignedCallerAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.HandleAsync(Request(), caller, CancellationToken.None));

        var user = await db.Users.SingleAsync(u => u.Id == caller.UserId);
        user.SetSignature(SignatureTestImages.CreatePngDataUrl(), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        await handler.HandleAsync(Request(), caller, CancellationToken.None);

        Assert.Single(await db.LoanApplications.ToListAsync());
    }

    private sealed class FakeSettingsStore(SettingSnapshot snapshot) : ISystemSettingsStore
    {
        public Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default)
            => Task.FromResult(new SettingSnapshot(value, DateTime.UtcNow, "Admin"));
    }
}
