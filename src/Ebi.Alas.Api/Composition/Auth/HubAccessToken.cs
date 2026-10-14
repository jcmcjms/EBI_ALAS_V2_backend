namespace Ebi.Alas.Api.Composition.Auth;

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
