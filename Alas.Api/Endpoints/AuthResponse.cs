using Alas.Application.Common.Security;

namespace Alas.Api.Endpoints;

public sealed record AuthResponse(
    string AccessToken,
    int AccessTokenExpirationSeconds,
    AuthUserDto User);
