using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class UserAvailabilityTests
{
    [Fact]
    public void Create_DefaultsToAvailable()
    {
        var user = User.Create(
            "u1", "hash", "A", "B", "", "a@x.com", "011",
            UserRole.Evaluator, DateTimeOffset.UtcNow, "Credit Checker");

        Assert.True(user.IsAvailable);
    }

    [Fact]
    public void SetAvailable_False_MarksUnavailable()
    {
        var user = User.Create(
            "u2", "hash", "A", "B", "", "b@x.com", "011",
            UserRole.Approver, DateTimeOffset.UtcNow, "Branch Head");
        var now = DateTimeOffset.UtcNow;

        user.SetAvailable(false, now);

        Assert.False(user.IsAvailable);
        Assert.Equal(now, user.UpdatedAt);
    }

    [Fact]
    public void SetAvailable_SameValue_IsNoOp()
    {
        var user = User.Create(
            "u3", "hash", "A", "B", "", "c@x.com", "011",
            UserRole.Approver, DateTimeOffset.UtcNow, "Branch Head");
        var created = user.UpdatedAt;

        user.SetAvailable(true, DateTimeOffset.UtcNow.AddHours(1));

        Assert.True(user.IsAvailable);
        Assert.Equal(created, user.UpdatedAt);
    }
}
