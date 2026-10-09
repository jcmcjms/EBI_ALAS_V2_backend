using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.Submit;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
        LoanType.New,
        1000m,
        30,
        0.05m);

    [Fact]
    public async Task SubmitLoan_NonAdmin_UsesCallerBranch_IgnoresRequestBranch()
    {
        await using var db = Db();
        var handler = new SubmitLoanHandler(
            db,
            new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
            new Ebi.Alas.Api.Features.Workflow.WorkflowQueueService(db, TimeProvider.System),
            TimeProvider.System);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Encoder, "BR-A");

        var result = await handler.HandleAsync(Request("BR-HACK"), caller, CancellationToken.None);

        Assert.Equal("BR-A", result.BranchId);
    }

    [Fact]
    public async Task SubmitLoan_Admin_MayUseRequestBranch()
    {
        await using var db = Db();
        var handler = new SubmitLoanHandler(
            db,
            new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
            new Ebi.Alas.Api.Features.Workflow.WorkflowQueueService(db, TimeProvider.System),
            TimeProvider.System);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-HQ");

        var result = await handler.HandleAsync(Request("BR-OTHER"), caller, CancellationToken.None);

        Assert.Equal("BR-OTHER", result.BranchId);
    }

    [Fact]
    public async Task SubmitLoan_AdminWithoutRequestBranch_UsesCallerBranch()
    {
        await using var db = Db();
        var handler = new SubmitLoanHandler(
            db,
            new Ebi.Alas.Api.Features.LoanComputation.LoanComputationService(),
            new Ebi.Alas.Api.Features.Workflow.WorkflowQueueService(db, TimeProvider.System),
            TimeProvider.System);
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Admin, "BR-HQ");

        var result = await handler.HandleAsync(Request("  "), caller, CancellationToken.None);

        Assert.Equal("BR-HQ", result.BranchId);
    }
}
