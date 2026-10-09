namespace Ebi.Alas.Api.Composition.Auth;

/// <summary>
/// WebSocket transports cannot send an Authorization header, so SignalR clients
/// pass the JWT as <c>access_token</c>. Bearer auth only reads the header by default.
/// </summary>
public static class HubAccessToken
{
    public static string? Resolve(string path, string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        return new Microsoft.AspNetCore.Http.PathString(path)
            .StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase)
            ? accessToken
            : null;
    }
}
