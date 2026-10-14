using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class UserPasswordResetSentAtTests
{
    private static User CreateUser() => User.Create(
        "ada",
        "hash",
        "Ada",
        "Lovelace",
        "",
        "ada@example.com",
        "011",
        UserRole.Encoder,
        DateTimeOffset.UtcNow);

    [Fact]
    public void MarkPasswordResetSent_SetsTimestamp()
    {
        var user = CreateUser();
        var now = DateTimeOffset.UtcNow;

        user.MarkPasswordResetSent(now);

        Assert.Equal(now, user.PasswordResetSentAt);
    }

    [Fact]
    public void IsPasswordResetCooldownActive_WithinWindow_True()
    {
        var user = CreateUser();
        var start = DateTimeOffset.UtcNow;
        user.MarkPasswordResetSent(start);

        var active = user.IsPasswordResetCooldownActive(start.AddMinutes(5), TimeSpan.FromMinutes(15));

        Assert.True(active);
    }

    [Fact]
    public void IsPasswordResetCooldownActive_AfterWindow_False()
    {
        var user = CreateUser();
        var start = DateTimeOffset.UtcNow;
        user.MarkPasswordResetSent(start);

        var active = user.IsPasswordResetCooldownActive(start.AddMinutes(16), TimeSpan.FromMinutes(15));

        Assert.False(active);
    }

    [Fact]
    public void IsPasswordResetCooldownActive_NeverSent_False()
    {
        var user = CreateUser();

        var active = user.IsPasswordResetCooldownActive(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15));

        Assert.False(active);
    }
}
