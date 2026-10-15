using Ebi.Alas.Api.Features.LoanApplications.Domain;
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
}
