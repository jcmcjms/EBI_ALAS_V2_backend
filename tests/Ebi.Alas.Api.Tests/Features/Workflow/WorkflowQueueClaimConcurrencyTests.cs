using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Features.Workflow;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ebi.Alas.Api.Tests.Features.Workflow;

public sealed class WorkflowQueueClaimConcurrencyTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task<SqliteConnection> OpenSharedConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static AlasDbContext Db(SqliteConnection connection, SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AlasDbContext>().UseSqlite(connection);
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new AlasDbContext(builder.Options);
    }

    private static LoanApplication NewLoan(string lamId, string branchId, DateTimeOffset now) =>
        LoanApplication.Create(lamId, "GRP-1", "Client", branchId, LoanType.New, 1000m, 30, 0.05m, 50m, 10m, 940m, Guid.NewGuid(), now);

    [Fact]
    public async Task ClaimByIdAsync_SecondCallerAfterClaim_GetsConflict()
    {
        await using var connection = await OpenSharedConnectionAsync();
        await using var dbA = Db(connection);
        await using var dbB = Db(connection);
        await dbA.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var loan = NewLoan("LAM-RACE", "011", now);
        dbA.LoanApplications.Add(loan);
        var queueA = new WorkflowQueueService(dbA, new FixedTimeProvider(now));
        await queueA.EnqueueAsync(loan.Id, WorkflowStage.Recommendation, "011", CancellationToken.None);

        var deskA = new DeskQueueService(dbA, queueA, new FixedTimeProvider(now));
        var deskB = new DeskQueueService(dbB, new WorkflowQueueService(dbB, new FixedTimeProvider(now)), new FixedTimeProvider(now));
        var callerA = new CallerContext(Guid.NewGuid(), UserRole.Recommender, "011");
        var callerB = new CallerContext(Guid.NewGuid(), UserRole.Recommender, "011");

        var first = await deskA.ClaimByIdAsync(loan.Id, callerA, CancellationToken.None);
        Assert.NotNull(first);

        var second = await Assert.ThrowsAsync<ConflictException>(() =>
            deskB.ClaimByIdAsync(loan.Id, callerB, CancellationToken.None));
        Assert.Equal("Currently with another officer.", second.Message);

        await using var verify = Db(connection);
        var persisted = await verify.WorkflowQueueItems.SingleAsync(q => q.LoanApplicationId == loan.Id);
        Assert.Equal(callerA.UserId, persisted.OwnerUserId);
        Assert.Equal(QueueItemState.Active, persisted.State);
    }

    [Fact]
    public async Task ConcurrentPersist_RowVersion_BlocksSecondWriter()
    {
        await using var connection = await OpenSharedConnectionAsync();
        await using var dbA = Db(connection);
        await using var dbB = Db(connection);
        await dbA.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var queueA = new WorkflowQueueService(dbA, new FixedTimeProvider(now));
        await queueA.EnqueueAsync(Guid.NewGuid(), WorkflowStage.Recommendation, "011", CancellationToken.None);

        // Both contexts materialize the same queued row, then both persist a claim.
        var itemA = await dbA.WorkflowQueueItems.SingleAsync();
        var itemB = await dbB.WorkflowQueueItems.SingleAsync();
        Assert.True(itemA.TryClaim(Guid.NewGuid(), now));
        Assert.True(itemB.TryClaim(Guid.NewGuid(), now));

        await dbA.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());

        // The row remains leased by the first writer only.
        await using var verify = Db(connection);
        var persisted = await verify.WorkflowQueueItems.SingleAsync();
        Assert.Equal(itemA.OwnerUserId, persisted.OwnerUserId);
        Assert.Equal(QueueItemState.Active, persisted.State);
    }

    [Fact]
    public async Task ClaimNextAsync_WhenSaveThrowsConcurrency_MapsToConflict()
    {
        await using var connection = await OpenSharedConnectionAsync();
        var interceptor = new ThrowConcurrencyInterceptor();
        await using var db = Db(connection, interceptor);
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        await new WorkflowQueueService(db, new FixedTimeProvider(now))
            .EnqueueAsync(Guid.NewGuid(), WorkflowStage.Recommendation, "011", CancellationToken.None);

        interceptor.Armed = true;
        var queue = new WorkflowQueueService(db, new FixedTimeProvider(now));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            queue.ClaimNextAsync(WorkflowStage.Recommendation, "011", Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("Queue item was claimed concurrently.", ex.Message);
    }

    [Fact]
    public async Task DeskClaimByIdAsync_WhenSaveThrowsConcurrency_MapsToConflict()
    {
        await using var connection = await OpenSharedConnectionAsync();
        var interceptor = new ThrowConcurrencyInterceptor();
        await using var db = Db(connection, interceptor);
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var loan = NewLoan("LAM-MAP", "011", now);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();
        await new WorkflowQueueService(db, new FixedTimeProvider(now))
            .EnqueueAsync(loan.Id, WorkflowStage.Recommendation, "011", CancellationToken.None);

        interceptor.Armed = true;
        var desk = new DeskQueueService(db, new WorkflowQueueService(db, new FixedTimeProvider(now)), new FixedTimeProvider(now));
        var caller = new CallerContext(Guid.NewGuid(), UserRole.Recommender, "011");

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            desk.ClaimByIdAsync(loan.Id, caller, CancellationToken.None));
        Assert.Equal("Queue item was claimed concurrently.", ex.Message);
    }

    private sealed class ThrowConcurrencyInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                throw new DbUpdateConcurrencyException("Simulated concurrent claim.");
            }

            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            if (Armed)
            {
                throw new DbUpdateConcurrencyException("Simulated concurrent claim.");
            }

            return result;
        }
    }
}
