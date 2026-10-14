using Ebi.Alas.Api.Features.Auth;
using Ebi.Alas.Api.Features.Auth.ForgotPassword;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Email;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class ForgotPasswordHandlerTests
{
    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string TextBody)> Sent { get; } = [];
        public bool ThrowOnSend { get; set; }

        public Task SendAsync(string to, string subject, string textBody, string htmlBody, CancellationToken cancellationToken)
        {
            if (ThrowOnSend)
            {
                throw new EmailSendException("smtp down");
            }

            Sent.Add((to, subject, textBody));
            return Task.CompletedTask;
        }
    }

    private static AlasDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AlasDbContext>()
            .UseInMemoryDatabase($"forgot-pw-{Guid.NewGuid():N}")
            .Options;
        return new AlasDbContext(options);
    }

    private static ForgotPasswordHandler Handler(AlasDbContext db, IEmailSender email, TimeProvider timeProvider) => new(
        db,
        new PasswordHasher(),
        email,
        timeProvider,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ForgotPasswordHandler>.Instance);

    private static async Task<User> SeedUserAsync(AlasDbContext db, string? email = "ada@example.com")
    {
        var user = User.Create(
            "ada",
            new PasswordHasher().Hash("OldPass!1"),
            "Ada",
            "Lovelace",
            "",
            email,
            "011",
            UserRole.Encoder,
            DateTimeOffset.UtcNow);
        if (email is null)
        {
            user.UpdateContact(string.Empty, null, null, DateTimeOffset.UtcNow);
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task HandleAsync_UnknownUser_ReturnsAccountNotFound()
    {
        await using var db = Db();
        var email = new FakeEmailSender();

        var result = await Handler(db, email, TimeProvider.System).HandleAsync(
            new ForgotPasswordRequest { UserName = "ghost" },
            CancellationToken.None);

        var failure = Assert.IsType<ForgotPasswordOutcome.Failure>(result);
        Assert.Equal(StatusCodes.Status404NotFound, failure.StatusCode);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task HandleAsync_MissingEmail_ReturnsMissingEmail()
    {
        await using var db = Db();
        await SeedUserAsync(db, email: null);
        var email = new FakeEmailSender();

        var result = await Handler(db, email, TimeProvider.System).HandleAsync(
            new ForgotPasswordRequest { UserName = "ada" },
            CancellationToken.None);

        var failure = Assert.IsType<ForgotPasswordOutcome.Failure>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task HandleAsync_SendFails_DoesNotPersistPasswordHash()
    {
        await using var db = Db();
        var user = await SeedUserAsync(db);
        var originalHash = user.PasswordHash;
        var email = new FakeEmailSender { ThrowOnSend = true };

        var result = await Handler(db, email, TimeProvider.System).HandleAsync(
            new ForgotPasswordRequest { UserName = "ada" },
            CancellationToken.None);

        var failure = Assert.IsType<ForgotPasswordOutcome.Failure>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, failure.StatusCode);

        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(originalHash, stored.PasswordHash);
    }

    [Fact]
    public async Task HandleAsync_Success_SendsEmailAndRotatesPassword()
    {
        await using var db = Db();
        var user = await SeedUserAsync(db);
        var email = new FakeEmailSender();
        var hasher = new PasswordHasher();

        var result = await Handler(db, email, TimeProvider.System).HandleAsync(
            new ForgotPasswordRequest { UserName = "ada" },
            CancellationToken.None);

        Assert.IsType<ForgotPasswordOutcome.Success>(result);
        Assert.Single(email.Sent);
        Assert.Equal("ada@example.com", email.Sent[0].To);
        Assert.Contains("temporary password", email.Sent[0].Subject, StringComparison.OrdinalIgnoreCase);

        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.True(stored.MustChangePassword);
        Assert.NotNull(stored.PasswordResetSentAt);
        Assert.False(hasher.Verify("OldPass!1", stored.PasswordHash));
    }

    [Fact]
    public async Task HandleAsync_CooldownActive_ReturnsCooldown()
    {
        await using var db = Db();
        await SeedUserAsync(db);
        var email = new FakeEmailSender();
        var handler = Handler(db, email, TimeProvider.System);

        var first = await handler.HandleAsync(new ForgotPasswordRequest { UserName = "ada" }, CancellationToken.None);
        Assert.IsType<ForgotPasswordOutcome.Success>(first);

        var second = await handler.HandleAsync(new ForgotPasswordRequest { UserName = "ada" }, CancellationToken.None);
        var failure = Assert.IsType<ForgotPasswordOutcome.Failure>(second);
        Assert.Equal(StatusCodes.Status429TooManyRequests, failure.StatusCode);
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task HandleAsync_Success_RevokesRefreshTokens()
    {
        await using var db = Db();
        var user = await SeedUserAsync(db);
        var store = new TokenStore(db, TimeProvider.System);
        var (token, _) = await store.IssueRefreshTokenAsync(
            user.Id,
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(14),
            CancellationToken.None);
        var email = new FakeEmailSender();

        await Handler(db, email, TimeProvider.System).HandleAsync(
            new ForgotPasswordRequest { UserName = "ada" },
            CancellationToken.None);

        var stored = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == token.Id);
        Assert.False(stored.IsUsable(TimeProvider.System.GetUtcNow()));
    }
}
