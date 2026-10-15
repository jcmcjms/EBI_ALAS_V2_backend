using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.LoanApplications.Documents;
using Ebi.Alas.Api.Features.Documents;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class DocumentRemarkEndpointTests
{
    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"doc-remarks-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private sealed class CapturingNotifier : IRealtimeNotifier
    {
        public List<(Guid UserId, string Title, string? Link)> Sent { get; } = [];

        public Task NotifyUserAsync(
            Guid notificationId,
            Guid userId,
            string title,
            string body,
            string? link,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            Sent.Add((userId, title, link));
            return Task.CompletedTask;
        }

        public Task NotifyDashboardUpdatedAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task NotifyLoanAssignedAsync(
            Guid loanId,
            Guid userId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void DocumentRemark_Create_PersistsThreadFields()
    {
        var loanId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var parent = DocumentRemark.Create(
            loanId, "CIBI", 12, null, "Missing CIBI", authorId, DateTimeOffset.UtcNow);
        var reply = DocumentRemark.Create(
            loanId, "CIBI", 12, parent.Id, "Will upload", authorId, DateTimeOffset.UtcNow);

        Assert.Equal("CIBI", reply.ChecklistIdCode);
        Assert.Equal(12, reply.DocId);
        Assert.Equal(parent.Id, reply.ParentRemarkId);
        Assert.Equal("Will upload", reply.Body);
    }

    [Fact]
    public async Task PostThenGet_ReturnsStoredRemark()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var loan = LoanApplication.Create(
            "LAM-DOC", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now);
        db.Users.Add(encoder);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var remark = DocumentRemark.Create(
            loan.Id, "PAYSIP", 99, null, "Please clarify this payslip", encoder.Id, now);
        db.DocumentRemarks.Add(remark);
        await db.SaveChangesAsync();

        var loaded = await db.DocumentRemarks.AsNoTracking()
            .Where(r => r.LoanApplicationId == loan.Id)
            .ToListAsync();

        var item = Assert.Single(loaded);
        Assert.Equal("PAYSIP", item.ChecklistIdCode);
        Assert.Equal("Please clarify this payslip", item.Body);
        Assert.Equal(encoder.Id, item.AuthorId);
    }

    [Fact]
    public async Task CreateAsync_NotifiesEncoderAndParentAuthor()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var evaluator = User.Create("ben", "reyes", "Ben", "Reyes", "", "ben@example.com", "BR-A", UserRole.Evaluator, now, "CI");
        var loan = LoanApplication.Create(
            "LAM-DOC-N", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now);
        db.Users.AddRange(encoder, evaluator);
        db.LoanApplications.Add(loan);
        var parent = DocumentRemark.Create(
            loan.Id, "CIBI", 1, null, "Missing CIBI", evaluator.Id, now);
        db.DocumentRemarks.Add(parent);
        await db.SaveChangesAsync();

        var notifier = new CapturingNotifier();
        var service = new DocumentRemarkService(db, notifier, TimeProvider.System);

        await service.CreateAsync(
            loan,
            new CreateDocumentRemarkRequest("CIBI", 1, parent.Id, "Will upload tomorrow"),
            encoder.Id,
            CancellationToken.None);

        Assert.Contains(notifier.Sent, s =>
            s.UserId == evaluator.Id
            && s.Title == "New document remark"
            && s.Link == $"/loans/approval/{loan.Id}");
        Assert.Contains(notifier.Sent, s => s.UserId == evaluator.Id);
        Assert.DoesNotContain(notifier.Sent, s => s.UserId == encoder.Id);
    }

    [Fact]
    public async Task CreateAsync_DoesNotNotifySelfWhenPosterIsOnlyParticipant()
    {
        await using var db = Db();
        var now = DateTimeOffset.UtcNow;
        var encoder = User.Create("ana", "cruz", "Ana", "Cruz", "", "ana@example.com", "BR-A", UserRole.Encoder, now, "Clerk");
        var loan = LoanApplication.Create(
            "LAM-DOC-S", "G-1", "Client", "BR-A", LoanType.New,
            1000m, 30, 0.05m, 50m, 10m, 940m, encoder.Id, now);
        db.Users.Add(encoder);
        db.LoanApplications.Add(loan);
        await db.SaveChangesAsync();

        var notifier = new CapturingNotifier();
        var service = new DocumentRemarkService(db, notifier, TimeProvider.System);

        await service.CreateAsync(
            loan,
            new CreateDocumentRemarkRequest("CIBI", 1, null, "First note"),
            encoder.Id,
            CancellationToken.None);

        Assert.Empty(notifier.Sent);
    }
}
