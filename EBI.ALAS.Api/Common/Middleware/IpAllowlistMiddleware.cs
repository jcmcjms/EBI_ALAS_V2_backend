using System.Net;
using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Common.Middleware;

/// <summary>
/// IP allowlisting middleware for admin endpoints.
/// Restricts access to sensitive routes (admin, user management)
/// to configured IP addresses or CIDR ranges.
/// </summary>
public sealed class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpAllowlistMiddleware> _logger;
    private readonly HashSet<string> _allowedIps;
    private readonly List<(IPAddress Network, int PrefixLength)> _allowedCidrs;
    private readonly HashSet<string> _protectedPaths;

    public IpAllowlistMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<IpAllowlistMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        // Load allowed IPs from configuration
        var allowedIps = configuration.GetSection("IpAllowlist:AdminEndpoints").Get<string[]>() ?? Array.Empty<string>();

        _allowedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _allowedCidrs = new List<(IPAddress, int)>();

        foreach (var entry in allowedIps)
        {
            if (entry.Contains('/'))
            {
                // CIDR notation (e.g., "192.168.0.0/16")
                var parts = entry.Split('/');
                if (IPAddress.TryParse(parts[0], out var network) && int.TryParse(parts[1], out var prefix))
                {
                    _allowedCidrs.Add((network, prefix));
                }
            }
            else
            {
                // Single IP
                _allowedIps.Add(entry);
            }
        }

        // Paths that require IP allowlisting
        _protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/api/users",
            "/api/roles",
            "/api/workflow",
            "/api/audit-logs"
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only check IPs for protected admin endpoints
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
    {
        return _protectedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAllowed(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress))
            return false;

        // Check exact match
        if (_allowedIps.Contains(ipAddress))
            return true;

        // Check CIDR ranges
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

        for (int i = 0; i < fullBytes; i++)
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
        // Check X-Forwarded-For header first (for reverse proxies)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
