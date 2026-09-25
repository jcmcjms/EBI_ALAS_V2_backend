using EBI.ALAS.Api.Features.Loans;
using Xunit;

namespace EBI.ALAS.Tests;

public class QueueOwnershipAndClaimTests
{
    [Fact]
    public void ClaimByIdResult_Shape_Is_Extensible()
    {
        ClaimByIdResult result = new ClaimByIdResult.NotFound();
        Assert.IsType<ClaimByIdResult.NotFound>(result);
    }
}