using System.Security.Claims;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Tests.Composition.Auth;

public sealed class CallerContextTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth"));

    [Fact]
    public void Require_WithValidClaims_ReturnsContext()
    {
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("role", nameof(UserRole.Encoder)),
            new Claim("branch", "BR-01"));

        var context = CallerContext.Require(user);

        Assert.Equal(UserRole.Encoder, context.Role);
        Assert.Equal("BR-01", context.BranchId);
    }

    [Fact]
    public void Require_MissingUserId_ThrowsForbidden()
    {
        var user = Principal(
            new Claim("role", nameof(UserRole.Encoder)),
            new Claim("branch", "BR-01"));

        Assert.Throws<ForbiddenException>(() => CallerContext.Require(user));
    }

    [Fact]
    public void Require_InvalidUserId_ThrowsForbidden()
    {
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, "not-a-guid"),
            new Claim("role", nameof(UserRole.Encoder)),
            new Claim("branch", "BR-01"));

        Assert.Throws<ForbiddenException>(() => CallerContext.Require(user));
    }

    [Fact]
    public void Require_EmptyUserId_ThrowsForbidden()
    {
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()),
            new Claim("role", nameof(UserRole.Encoder)),
            new Claim("branch", "BR-01"));

        Assert.Throws<ForbiddenException>(() => CallerContext.Require(user));
    }

    [Fact]
    public void Require_MissingRole_ThrowsForbidden()
    {
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("branch", "BR-01"));

        Assert.Throws<ForbiddenException>(() => CallerContext.Require(user));
    }

    [Fact]
    public void Require_InvalidRole_ThrowsForbidden()
    {
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("role", "NotARole"),
            new Claim("branch", "BR-01"));

        Assert.Throws<ForbiddenException>(() => CallerContext.Require(user));
    }

    [Fact]
    public void Require_MissingBranch_ThrowsForbidden()
    {
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("role", nameof(UserRole.Encoder)));

        Assert.Throws<ForbiddenException>(() => CallerContext.Require(user));
    }

    [Fact]
    public void Require_AdminRole_SetsCanAccessAllBranches()
    {
        var user = Principal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("role", nameof(UserRole.Admin)),
            new Claim("branch", "BR-01"));

        var context = CallerContext.Require(user);

        Assert.True(context.CanAccessAllBranches);
    }
}
