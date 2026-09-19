using System.Net;
using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// IP allowlisting middleware for admin endpoints.
/// Restricts access to sensitive routes to configured IP addresses or CIDR ranges.
/// </summary>
public sealed class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpAllowlistMiddleware> _logger;
    private readonly HashSet<string> _allowedIps = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(IPAddress Network, int PrefixLength)> _allowedCidrs = [];
    private readonly HashSet<string> _protectedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/users",
        "/api/roles",
        "/api/workflow",
        "/api/audit-logs"
    };

    public IpAllowlistMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<IpAllowlistMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        var allowedIps = configuration.GetSection("IpAllowlist:AdminEndpoints").Get<string[]>() ?? [];

        foreach (var entry in allowedIps)
        {
            if (entry.Contains('/'))
            {
                var parts = entry.Split('/');
                if (IPAddress.TryParse(parts[0], out var network) && int.TryParse(parts[1], out var prefix))
                    _allowedCidrs.Add((network, prefix));
            }
            else
            {
                _allowedIps.Add(entry);
            }
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (IsProtectedPath(path))
        {
            var clientIp = GetClientIpAddress(context);

            if (!IsAllowed(clientIp))
            {
                _logger.LogWarning(
                    "IP allowlist blocked request from {IpAddress} to {Path}",
                    clientIp, path);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(
                    ApiResponse.ErrorResponse("Access denied: your IP address is not authorized for this endpoint."));
                return;
            }
        }

        await _next(context);
    }

    private bool IsProtectedPath(string path)
        => _protectedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private bool IsAllowed(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress))
            return false;

        if (_allowedIps.Contains(ipAddress))
            return true;

        if (IPAddress.TryParse(ipAddress, out var clientAddr))
        {
            foreach (var (network, prefixLength) in _allowedCidrs)
            {
                if (IsInCidrRange(clientAddr, network, prefixLength))
                    return true;
            }
        }

        return false;
    }

    private static bool IsInCidrRange(IPAddress clientAddr, IPAddress network, int prefixLength)
    {
        var clientBytes = clientAddr.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();

        if (clientBytes.Length != networkBytes.Length)
            return false;

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (clientBytes[i] != networkBytes[i])
                return false;
        }

        if (remainingBits > 0 && fullBytes < clientBytes.Length)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((clientBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                return false;
        }

        return true;
    }

    private static string? GetClientIpAddress(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
