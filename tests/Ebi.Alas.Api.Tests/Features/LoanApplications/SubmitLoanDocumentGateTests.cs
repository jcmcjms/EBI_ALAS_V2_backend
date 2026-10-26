using System.Text.Json;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Documents;
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

public sealed class SubmitLoanDocumentGateTests
{
    private sealed class FakeChecklistReader : IChecklistDocumentReader
    {
        public Dictionary<string, HashSet<string>> UploadedByLoanNo { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string LoanNo, IReadOnlyCollection<string> Codes)> Calls { get; } = [];

        public Task<IReadOnlyList<LoanChecklistDocumentDto>> GetChecklistDocumentsAsync(
            string loanNo,
            IReadOnlyCollection<string> idCodes,
            CancellationToken cancellationToken)
        {
            Calls.Add((loanNo, idCodes.ToArray()));
            UploadedByLoanNo.TryGetValue(loanNo, out var uploaded);
            uploaded ??= [];
            IReadOnlyList<LoanChecklistDocumentDto> docs = idCodes
                .Select(c => new LoanChecklistDocumentDto(
                    loanNo, "P", c, c, uploaded.Contains(c) ? 1 : null, null, null, null, null, null,
                    uploaded.Contains(c) ? "Uploaded" : "No uploaded documents"))
                .ToList();
            return Task.FromResult(docs);
        }

        public Task<ChecklistDocumentContent?> GetDocumentFileAsync(int docId, CancellationToken cancellationToken) =>
            Task.FromResult<ChecklistDocumentContent?>(null);
    }

    private static AlasDbContext Db() => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"submit-docs-{Guid.NewGuid():N}")
            .Options);

    private static async Task SeedRequirementsAsync(AlasDbContext db)
    {
        foreach (var (productCode, loanType, code, description) in ProductDocumentRequirementCatalog.Defaults)
        {
            db.LoanProductDocumentRequirements.Add(
                LoanProductDocumentRequirement.Create(productCode, loanType, code, description));
        }

        await db.SaveChangesAsync();
    }

    private static SubmitLoanHandler Handler(AlasDbContext db, IChecklistDocumentReader reader) => new(
        db,
        new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
        new WorkflowQueueService(db, TimeProvider.System),
        new WorkflowConfiguration(
            Options.Create(new WorkflowOptions()).ToMonitor(),
            new FakeSettingsStore(new SettingSnapshot(false, DateTime.UtcNow, "Admin"))),
        new ApplicationNumberGenerator(db),
        new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
        reader,
        new FakeWebLoanReader(),
        realtime: null,
        TimeProvider.System);

    private static string FormWithLoans(params string?[] loanNos)
    {
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var loans = string.Join(
            ",",
            loanNos.Select(n => n is null
                ? "{\"parameters\":{\"nthpDate\":\"" + today + "\"}}"
                : "{\"loanNo\":\"" + n + "\",\"parameters\":{\"nthpDate\":\"" + today + "\"}}"));
        return "{\"client\":{\"cisId\":\"0880530879\",\"address\":\"123 Rizal St\"},\"loans\":[" + loans + "]}";
    }

    private static SubmitLoanApplicationsRequest Request(
        LoanType loanType,
        string? formJson,
        params SubmitLoanLine[] loans) => new(
        "G-1",
        "Client",
        "BR-A",
        loanType,
        loans,
        formJson is null ? null : JsonDocument.Parse(formJson).RootElement.Clone());

    [Fact]
    public async Task Submit_ApdsNew_AllRequiredUploaded_Succeeds()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                ["PN-1"] = ["A1004", "CCR38", "A2016", "A2008", "PIC03", "A2020"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(LoanType.New, FormWithLoans("PN-1"),
                new SubmitLoanLine("A16", "A16", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.Single(result.Loans);
        Assert.Single(await db.LoanApplications.ToListAsync());
    }

    [Fact]
    public async Task Submit_ApdsNew_MissingCibi_BlocksAndCreatesNothing()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                ["PN-1"] = ["A1004", "CCR38", "A2016", "A2008", "PIC03"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(LoanType.New, FormWithLoans("PN-1"),
                new SubmitLoanLine("A16", "A16", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Contains(result.MissingDocuments, e => e.Contains("CIBI Report"));
        Assert.DoesNotContain(result.MissingDocuments, e => e.Contains("A2020"));
        Assert.Empty(await db.LoanApplications.ToListAsync());
    }

    [Fact]
    public async Task Submit_RelLoanApds_UsesReloanRequirements_NotNew()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                // APDS Reloan required: CCR38, A2016, A2008 only
                ["PN-1"] = ["CCR38", "A2016", "A2008"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        // request.LoanType is wrongly New; form loan is Reloan (creationTypeCode 1)
        var result = await handler.HandleAsync(
            Request(
                LoanType.New,
                """{"client":{"cisId":"0880530879","address":"123 Rizal St"},"loans":[{"loanNo":"PN-1","creationTypeCode":1,"parameters":{"nthpDate":"2026-10-01"}}]}""",
                new SubmitLoanLine("A17", "A17", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.False(result.IsBlocked);
        Assert.Single(await db.LoanApplications.ToListAsync());
    }

    [Fact]
    public async Task Submit_AdditionalLoanApds_UsesReloanRequirements()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                ["PN-1"] = ["CCR38", "A2016", "A2008"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(
                LoanType.New,
                """{"client":{"cisId":"0880530879","address":"123 Rizal St"},"loans":[{"loanNo":"PN-1","creationTypeCode":6,"parameters":{"nthpDate":"2026-10-01"}}]}""",
                new SubmitLoanLine("A17", "A17", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.False(result.IsBlocked);
    }

    [Fact]
    public async Task Submit_MissingDocs_ReturnsReadableErrorList()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                ["PN-1"] = ["A1004", "CCR38", "A2016", "A2008", "PIC03"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(LoanType.New, FormWithLoans("PN-1"),
                new SubmitLoanLine("A16", "A16", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Contains("PN-1 (A16): CIBI Report is missing", result.MissingDocuments);
        var message = SubmitLoanHandler.BuildMissingDocumentsMessage(result.MissingDocuments);
        Assert.Contains("PN-1 (A16): CIBI Report is missing", message);
        Assert.DoesNotContain("A2020", message);
    }

    [Fact]
    public async Task Submit_MultiLoan_MissingDocs_LabelsEachApplication()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                ["PN-1"] = ["A1004", "CCR38", "A2016", "A2008", "PIC03", "A2020"],
                ["PN-2"] = ["A1004", "CCR38", "A2016", "A2008", "PIC03"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(
                LoanType.New,
                FormWithLoans("PN-1", "PN-2"),
                new SubmitLoanLine("A16", "A16", 1000m, 30, 0.05m),
                new SubmitLoanLine("A16", "A16", 2000m, 60, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Contains("PN-2 (A16): CIBI Report is missing", result.MissingDocuments);
        Assert.DoesNotContain(result.MissingDocuments, e => e.StartsWith("PN-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Submit_ApdsReloan_DoesNotRequireNewOnlyDocs()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                ["PN-1"] = ["CCR38", "A2016", "A2008"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(LoanType.Reloan, FormWithLoans("PN-1"),
                new SubmitLoanLine("A16", "A16", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.Single(result.Loans);
    }

    [Fact]
    public async Task Submit_C21New_MissingA2027_Blocks()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                ["PN-1"] = ["A1004", "CCR38", "A2008", "PIC03", "A2020", "A2023"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(LoanType.New, FormWithLoans("PN-1"),
                new SubmitLoanLine("C21", "C21", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Contains(result.MissingDocuments, e => e.Contains("Others Required Docs"));
        Assert.Empty(await db.LoanApplications.ToListAsync());
    }

    [Fact]
    public async Task Submit_C21Reloan_RequiresA2027_NotPicOrCibi()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                ["PN-1"] = ["A1004", "CCR38", "A2008"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(LoanType.Reloan, FormWithLoans("PN-1"),
                new SubmitLoanLine("C21", "C21", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Contains(result.MissingDocuments, e => e.Contains("Others Required Docs"));
        Assert.DoesNotContain(result.MissingDocuments, e => e.Contains("CLIENT SELFIE"));
        Assert.DoesNotContain(result.MissingDocuments, e => e.Contains("CIBI"));
    }

    [Fact]
    public async Task Submit_C02New_MissingCashFlow_Blocks()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                ["PN-1"] = ["A1004", "A2008"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(LoanType.New, FormWithLoans("PN-1"),
                new SubmitLoanLine("C02", "C02", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Contains(result.MissingDocuments, e => e.Contains("CASH FLOW"));
    }

    [Fact]
    public async Task Submit_MissingLoanNo_TreatsAllRequiredMissing()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader();
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(LoanType.New, FormWithLoans((string?)null),
                new SubmitLoanLine("A16", "A16", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Contains(result.MissingDocuments, e => e.Contains("Duly Accomplished Application Form"));
        Assert.Empty(await db.LoanApplications.ToListAsync());
    }

    [Fact]
    public async Task Submit_MultiLoan_OneIncomplete_RejectsEntireRequest()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader
        {
            UploadedByLoanNo =
            {
                ["PN-1"] = ["A1004", "CCR38", "A2016", "A2008", "PIC03", "A2020"],
                ["PN-2"] = ["A1004"],
            },
        };
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(LoanType.New, FormWithLoans("PN-1", "PN-2"),
                new SubmitLoanLine("A16", "A16", 1000m, 30, 0.05m),
                new SubmitLoanLine("C02", "C02", 2000m, 15, 0.08m)),
            caller,
            CancellationToken.None);

        Assert.True(result.IsBlocked);
        Assert.Empty(await db.LoanApplications.ToListAsync());
    }

    [Fact]
    public async Task Submit_NoMatrixRows_SucceedsWithoutChecklistCall()
    {
        await using var db = Db();
        var reader = new FakeChecklistReader();
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);

        var result = await handler.HandleAsync(
            Request(LoanType.New, FormWithLoans((string?)null), new SubmitLoanLine("Z99", "Z99", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.Single(result.Loans);
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task Submit_AmortNoOne_TreatsRequiredDocsAsSubmitted()
    {
        await using var db = Db();
        await SeedRequirementsAsync(db);
        var reader = new FakeChecklistReader();
        var handler = Handler(db, reader);
        var caller = await TestUsers.SeedSignedCallerAsync(db);
        // Handler() uses FakeWebLoanReader — enable amort_no=1 via a custom handler below.
        var webLoan = new FakeWebLoanReader { HasFirstAmortization = true };
        var handlerWithAmort = new SubmitLoanHandler(
            db,
            new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
            new WorkflowQueueService(db, TimeProvider.System),
            new WorkflowConfiguration(
                Options.Create(new WorkflowOptions()).ToMonitor(),
                new FakeSettingsStore(new SettingSnapshot(false, DateTime.UtcNow, "Admin"))),
            new ApplicationNumberGenerator(db),
            new Ebi.Alas.Api.Features.Notifications.ApplicationArrivalNotifier(db, null, TimeProvider.System),
            reader,
            webLoan,
            realtime: null,
            TimeProvider.System);

        var result = await handlerWithAmort.HandleAsync(
            Request(LoanType.New, FormWithLoans("PN-1"),
                new SubmitLoanLine("A16", "A16", 1000m, 30, 0.05m)),
            caller,
            CancellationToken.None);

        Assert.False(result.IsBlocked);
        Assert.Single(result.Loans);
    }

    private sealed class FakeSettingsStore(SettingSnapshot snapshot) : ISystemSettingsStore
    {
        public Task<SettingSnapshot> GetSnapshotAsync(string key, CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<SettingSnapshot> SetBoolAsync(string key, bool value, Guid userId, CancellationToken ct = default)
            => Task.FromResult(new SettingSnapshot(value, DateTime.UtcNow, "Admin"));
    }
}
