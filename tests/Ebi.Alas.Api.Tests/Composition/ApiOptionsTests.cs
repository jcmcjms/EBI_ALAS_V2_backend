using System.ComponentModel.DataAnnotations;
using Ebi.Alas.Api.Composition;

namespace Ebi.Alas.Api.Tests.Composition;

public sealed class ApiOptionsTests
{
    [Fact]
    public void Validate_WhenJwtIssuerMissing_ReturnsError()
    {
        var options = CreateOptions(jwt: CreateJwt(issuer: ""));

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains("Jwt:Issuer"));
    }

    [Fact]
    public void Validate_WhenSigningKeyTooShort_ReturnsError()
    {
        var options = CreateOptions(jwt: CreateJwt(signingKey: "short"));

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains("Jwt:SigningKey"));
    }

    [Fact]
    public void Validate_WhenJwtAudienceMissing_ReturnsError()
    {
        var options = CreateOptions(jwt: CreateJwt(audience: ""));

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains("Jwt:Audience"));
    }

    [Fact]
    public void Validate_WhenAccessTokenMinutesLessThanOne_ReturnsError()
    {
        var options = CreateOptions(jwt: CreateJwt(accessTokenMinutes: 0));

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains("Jwt:AccessTokenMinutes"));
    }

    [Fact]
    public void Validate_WhenAccessTokenMinutesExceedsUpperBound_ReturnsError()
    {
        var options = CreateOptions(jwt: CreateJwt(accessTokenMinutes: 121));

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains("Jwt:AccessTokenMinutes"));
    }

    [Fact]
    public void Validate_WhenRefreshTokenDaysLessThanOne_ReturnsError()
    {
        var options = CreateOptions(jwt: CreateJwt(refreshTokenDays: 0));

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains("Jwt:RefreshTokenDays"));
    }

    [Fact]
    public void Validate_WhenRefreshTokenDaysExceedsUpperBound_ReturnsError()
    {
        var options = CreateOptions(jwt: CreateJwt(refreshTokenDays: 31));

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains("Jwt:RefreshTokenDays"));
    }

    [Fact]
    public void Validate_WhenRefreshTokenAbsoluteDaysLessThanRefreshTokenDays_ReturnsError()
    {
        var options = CreateOptions(jwt: CreateJwt(refreshTokenDays: 10, refreshTokenAbsoluteDays: 5));

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains("Jwt:RefreshTokenAbsoluteDays"));
    }

    [Fact]
    public void Validate_WhenDefaultPageSizeLessThanOne_ReturnsError()
    {
        var options = CreateOptions(defaultPageSize: 0);

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ApiOptions.DefaultPageSize)));
    }

    [Fact]
    public void Validate_WhenMaxPageSizeLessThanOne_ReturnsError()
    {
        var options = CreateOptions(maxPageSize: 0);

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ApiOptions.MaxPageSize)));
    }

    [Fact]
    public void Validate_WhenMaxPageSizeExceedsUpperBound_ReturnsError()
    {
        var options = CreateOptions(maxPageSize: 1001);

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ApiOptions.MaxPageSize)));
    }

    [Fact]
    public void Validate_WhenDefaultPageSizeExceedsMax_ReturnsError()
    {
        var options = CreateOptions(defaultPageSize: 50, maxPageSize: 10);

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ApiOptions.DefaultPageSize)));
    }

    [Fact]
    public void Validate_WhenJwtValidAndPaginationValid_ReturnsNoErrors()
    {
        var options = CreateOptions();

        var results = Validate(options);

        Assert.Empty(results);
    }

    private static List<ValidationResult> Validate(ApiOptions options)
        => [.. ((IValidatableObject)options).Validate(new ValidationContext(options))];

    private static ApiOptions CreateOptions(
        JwtOptions? jwt = null,
        int defaultPageSize = 20,
        int maxPageSize = 100)
        => new()
        {
            Jwt = jwt ?? CreateJwt(),
            DefaultPageSize = defaultPageSize,
            MaxPageSize = maxPageSize
        };

    private static JwtOptions CreateJwt(
        string issuer = "ebi-alas",
        string audience = "alas",
        string signingKey = "at-least-32-characters-signing-key!!",
        int accessTokenMinutes = 15,
        int refreshTokenDays = 7,
        int refreshTokenAbsoluteDays = 14)
        => new()
        {
            Issuer = issuer,
            Audience = audience,
            SigningKey = signingKey,
            AccessTokenMinutes = accessTokenMinutes,
            RefreshTokenDays = refreshTokenDays,
            RefreshTokenAbsoluteDays = refreshTokenAbsoluteDays
        };
}
