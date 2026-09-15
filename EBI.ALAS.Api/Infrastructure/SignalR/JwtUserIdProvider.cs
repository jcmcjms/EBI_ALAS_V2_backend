using Microsoft.AspNetCore.SignalR;

namespace EBI.ALAS.Api.Infrastructure.SignalR;

/// <summary>
/// Maps the "userId" claim minted in JwtTokenService to SignalR's
/// <see cref="HubConnectionContext"/> so
/// <c>IHubContext.Clients.User(id)</c> targets the correct connection.
///
/// Without this registration SignalR falls back to
/// <c>ClaimTypes.NameIdentifier</c> ("sub"), which also works — but
/// an explicit provider makes the contract visible and testable.
/// </summary>
public class JwtUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst("userId")?.Value;
    }
}
