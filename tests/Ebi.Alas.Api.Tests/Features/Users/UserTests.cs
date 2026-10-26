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
            firstName: "Ada",
            lastName: "Lovelace",
            middleName: "",
            email: "ada@example.com",
            branchId: "011",
            role: UserRole.Encoder,
            now: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_NullEmail_StoresEmptyEmail()
    {
        var user = User.Create(
            userName: "ada",
            passwordHash: "hash",
            firstName: "Ada",
            lastName: "Lovelace",
            middleName: "",
            email: null,
            branchId: "011",
            role: UserRole.Encoder,
            now: DateTimeOffset.UtcNow);

        Assert.Equal(string.Empty, user.Email);
    }

    [Fact]
    public void SetSignature_StoresSanitizedPngDataUrl()
    {
        var user = User.Create(
            "ada", "hash", "Ada", "Lovelace", "", "a@x.com", "011", UserRole.Encoder, DateTimeOffset.UtcNow);
        var signature = SignatureTestImages.CreatePngDataUrl();

        user.SetSignature(signature, DateTimeOffset.UtcNow);

        Assert.NotNull(user.ESignature);
        Assert.StartsWith("data:image/png;base64,", user.ESignature);
    }

    [Fact]
    public void SetSignature_Null_ClearsSignature()
    {
        var user = User.Create(
            "ada", "hash", "Ada", "Lovelace", "", "a@x.com", "011", UserRole.Encoder, DateTimeOffset.UtcNow);
        user.SetSignature(SignatureTestImages.CreatePngDataUrl(), DateTimeOffset.UtcNow);

        user.SetSignature(null, DateTimeOffset.UtcNow);

        Assert.Null(user.ESignature);
    }

    [Fact]
    public void SetSignature_RejectsNonDataUrl()
    {
        var user = User.Create(
            "ada", "hash", "Ada", "Lovelace", "", "a@x.com", "011", UserRole.Encoder, DateTimeOffset.UtcNow);

        Assert.ThrowsAny<ArgumentException>(() =>
            user.SetSignature("javascript:alert(1)", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SetSignature_RejectsSvgInk()
    {
        var user = User.Create(
            "ada", "hash", "Ada", "Lovelace", "", "a@x.com", "011", UserRole.Encoder, DateTimeOffset.UtcNow);

        Assert.ThrowsAny<ArgumentException>(() =>
            user.SetSignature("data:image/svg+xml;base64,PHN2Zz4=", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Suspend_ActiveUser_SetsSuspended()
    {
        var user = User.Create("ada", "hash", "Ada", "Ada", "", "ada@example.com", "011", UserRole.Encoder, DateTimeOffset.UtcNow);

        user.Suspend(DateTimeOffset.UtcNow);

        Assert.Equal(UserStatus.Suspended, user.Status);
    }

    [Fact]
    public void Suspend_AlreadySuspended_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("ada", "hash", "Ada", "Ada", "", "ada@example.com", "011", UserRole.Encoder, now);
        user.Suspend(now);

        Assert.Throws<InvalidOperationException>(() => user.Suspend(now));
    }

    [Fact]
    public void Activate_SuspendedUser_SetsActive()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("ada", "hash", "Ada", "Ada", "", "ada@example.com", "011", UserRole.Encoder, now);
        user.Suspend(now);

        user.Activate(now);

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void UpdateProfile_NullEmail_StoresEmptyEmail()
    {
        var user = User.Create("ada", "hash", "Ada", "Ada", "", "ada@example.com", "011", UserRole.Encoder, DateTimeOffset.UtcNow);

        user.UpdateProfile("Ada", "Lovelace", "", null, "011", UserRole.Encoder, DateTimeOffset.UtcNow);

        Assert.Equal(string.Empty, user.Email);
    }
}
