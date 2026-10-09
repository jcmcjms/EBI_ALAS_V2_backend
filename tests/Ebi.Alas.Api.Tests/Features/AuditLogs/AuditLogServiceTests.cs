using Ebi.Alas.Api.Features.AuditLogs;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.AuditLogs;

public sealed class AuditLogServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"audit-log-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    [Fact]
    public void Record_CapturesActorEntityAndSummary()
    {
        var now = DateTimeOffset.UtcNow;
        var log = AuditLog.Record(
            userId: Guid.NewGuid(),
            userName: "jdoe",
            action: "Update",
            entityType: "User",
            entityId: "42",
            entityLabel: "jdoe",
            summary: "Updated branch",
            rawChanges: """{"branchId":"011"}""",
            ipAddress: "10.0.0.1",
            userAgent: "test-agent",
            now: now);

        Assert.Equal("jdoe", log.UserName);
        Assert.Equal("Update", log.Action);
        Assert.Equal("User", log.EntityType);
        Assert.Equal("42", log.EntityId);
        Assert.Equal("jdoe", log.EntityLabel);
        Assert.Equal("Updated branch", log.Summary);
        Assert.Equal("""{"branchId":"011"}""", log.RawChanges);
        Assert.Equal("10.0.0.1", log.IpAddress);
        Assert.Equal("test-agent", log.UserAgent);
        Assert.Equal(now, log.Timestamp);
    }

    [Fact]
    public async Task LogAsync_PersistsEntry()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var service = new AuditLogService(db, new FixedTimeProvider(now));

        await service.LogAsync(
            userId: null,
            userName: "system",
            action: "Login",
            entityType: "Auth",
            entityId: string.Empty,
            entityLabel: "admin",
            summary: "User logged in",
            rawChanges: null,
            ipAddress: null,
            userAgent: null);

        var saved = await db.AuditLogs.SingleAsync();
        Assert.Equal("Login", saved.Action);
        Assert.Equal("system", saved.UserName);
        Assert.Equal(now, saved.Timestamp);
    }
}
