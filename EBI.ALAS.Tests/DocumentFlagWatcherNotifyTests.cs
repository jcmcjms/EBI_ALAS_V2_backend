using EBI.ALAS.Api.Shared.Time;
using EBI.ALAS.Api.Features.ApprovalMatrix;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.Notifications;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
namespace EBI.ALAS.Tests;
public class DocumentFlagWatcherNotifyTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotificationService _realtime = Substitute.For<IRealtimeNotificationService>();
    private readonly IDocumentCompletenessService _completeness = Substitute.For<IDocumentCompletenessService>();
    private readonly IDocumentChecklistStore _checklist = Substitute.For<IDocumentChecklistStore>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly DocumentFlagService _sut;

    public DocumentFlagWatcherNotifyTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        var time = Substitute.For<ITimeProvider>();
        time.UtcNow.Returns(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));
        var system = Substitute.For<ISystemPrincipal>();
        _sut = new DocumentFlagService(
            _context, _completeness, _checklist, _audit, time,
            _notifications, _realtime, system);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task FlagAsync_notifies_entity_watchers_excluding_actor_and_encoder()
    {
        var loan = new LoanApplication
        {
            LamId = "LAM-1",
            ApplicationGroupNo = "APP-1",
            BranchCode = "011",
            FirstName = "Test",
            LastName = "Borrower",
            LoanNo = "LN-1",
            ProductCode = "P1",
            Product = "Product",
            Status = "ForChecking",
            CreatedById = 3,
        };
        _context.LoanApplications.Add(loan);
        await _context.SaveChangesAsync();

        _completeness.GetItemsByLoanNoAsync("LN-1", Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.FlagAsync(loan, ["A2020"], "Missing ID", actorUserId: 7, CancellationToken.None);

        await _realtime.Received(1).NotifyEntityWatchersAsync(
            loan.Id,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Is<IReadOnlyCollection<int>>(ids =>
                ids.Contains(7) && ids.Contains(3)));
    }
}
