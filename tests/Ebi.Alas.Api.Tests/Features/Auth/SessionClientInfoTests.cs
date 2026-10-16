using Ebi.Alas.Api.Features.Auth;
using Microsoft.AspNetCore.Http;

namespace Ebi.Alas.Api.Tests.Features.Auth;

public sealed class SessionClientInfoTests
{
    [Fact]
    public void Parse_KnownChromeWindows_ProducesDeviceLabel()
    {
        var info = SessionClientInfo.Parse(
            ip: "203.0.113.10",
            userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");

        Assert.Equal("203.0.113.10", info.IpAddress);
        Assert.Equal("Chrome on Windows", info.DeviceInfo);
    }

    [Fact]
    public void Parse_UnknownAgent_ReturnsNullDevice()
    {
        var info = SessionClientInfo.Parse(ip: null, userAgent: null);
        Assert.Null(info.IpAddress);
        Assert.Null(info.DeviceInfo);
    }

    [Fact]
    public void FromHttpContext_UsesRemoteIpAndUserAgent()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.7");
        context.Request.Headers.UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

        var info = SessionClientInfo.From(context);

        Assert.Equal("198.51.100.7", info.IpAddress);
        Assert.Equal("Safari on iOS", info.DeviceInfo);
    }
}
