using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class UserTests
{
    [Fact]
    public void Create_RequiresUserName()
    {
        Assert.ThrowsAny<ArgumentException>(() => User.Create(
            userName: "  ",
            passwordHash: "hash",
            fullName: "Ada",
            email: "ada@example.com",
            branchId: "011",
            role: UserRole.Encoder,
            now: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Suspend_ActiveUser_SetsSuspended()
    {
        var user = User.Create("ada", "hash", "Ada", "ada@example.com", "011", UserRole.Encoder, DateTimeOffset.UtcNow);

        user.Suspend(DateTimeOffset.UtcNow);

        Assert.Equal(UserStatus.Suspended, user.Status);
    }

    [Fact]
    public void Suspend_AlreadySuspended_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("ada", "hash", "Ada", "ada@example.com", "011", UserRole.Encoder, now);
        user.Suspend(now);

        Assert.Throws<InvalidOperationException>(() => user.Suspend(now));
    }

    [Fact]
    public void Activate_SuspendedUser_SetsActive()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("ada", "hash", "Ada", "ada@example.com", "011", UserRole.Encoder, now);
        user.Suspend(now);

        user.Activate(now);

        Assert.Equal(UserStatus.Active, user.Status);
    }
}
