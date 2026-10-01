using Microsoft.AspNetCore.SignalR;
namespace EBI.ALAS.Api.Infrastructure.SignalR;
public class JwtUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst("userId")?.Value;
    }
}
