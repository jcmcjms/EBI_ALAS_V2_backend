using System.Text.Json;
using Ebi.Alas.Api.Features.Dashboard;

namespace Ebi.Alas.Api.Tests.Features.Dashboard;

public sealed class DashboardOverviewJsonTests
{
    [Fact]
    public void Serialize_DoesNotCollidePushbackCountAndList()
    {
        var overview = new DashboardOverview(
            1, 2, 3, 4, 5, 0,
            [],
            [],
            [],
            [new PushBackItem(1, "LAM-1", "001", DateTimeOffset.UtcNow, "Returned")],
            [],
            []);

        var json = JsonSerializer.Serialize(overview, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.Contains("\"pushbacks\":5", json);
        Assert.Contains("\"pushBackItems\"", json);
        Assert.DoesNotContain("\"pushBacks\":", json);
    }
}
