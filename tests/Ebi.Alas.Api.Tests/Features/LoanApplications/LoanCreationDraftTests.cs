using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class LoanCreationDraftTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AlasDbContext Db(string name) => new(
        new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    [Fact]
    public void Create_AssignsUserAndPayload()
    {
        var userId = Guid.NewGuid();
        var draft = LoanCreationDraft.Create(userId, "{\"client\":{}}", Now);

        Assert.Equal(userId, draft.UserId);
        Assert.Equal("{\"client\":{}}", draft.Payload);
        Assert.Equal(Now, draft.UpdatedAt);
    }

    [Fact]
    public void Create_OverMaxLength_Throws()
    {
        var payload = new string('x', LoanCreationDraft.MaxPayloadLength + 1);
        Assert.Throws<ArgumentException>(
            () => LoanCreationDraft.Create(Guid.NewGuid(), payload, Now));
    }

    [Fact]
    public void Update_ReplacesPayload()
    {
        var draft = LoanCreationDraft.Create(Guid.NewGuid(), "a", Now);
        draft.Update("b", Now.AddMinutes(1));
        Assert.Equal("b", draft.Payload);
        Assert.Equal(Now.AddMinutes(1), draft.UpdatedAt);
    }

    [Fact]
    public async Task SaveChanges_KeepsSingleRowPerUser()
    {
        var userId = Guid.NewGuid();

        var dbName = $"creation-draft-{Guid.NewGuid():N}";

        await using (var db = Db(dbName))
        {
            db.LoanCreationDrafts.Add(LoanCreationDraft.Create(userId, "one", Now));
            await db.SaveChangesAsync();
        }

        await using (var db = Db(dbName))
        {
            var draft = await db.LoanCreationDrafts.SingleAsync(d => d.UserId == userId);
            draft.Update("two", Now.AddMinutes(1));
            await db.SaveChangesAsync();
        }

        await using (var db = Db(dbName))
        {
            var rows = await db.LoanCreationDrafts.Where(d => d.UserId == userId).ToListAsync();
            Assert.Single(rows);
            Assert.Equal("two", rows[0].Payload);
        }
    }
}
