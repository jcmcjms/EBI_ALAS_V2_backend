using System.Text.Json;
using Ebi.Alas.Api.Features.LoanApplications.Submit;
using Ebi.Alas.Api.Features.WebLoans;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class CocreClientGateTests
{
    private static JsonElement Form(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task MissingCisId_Blocks()
    {
        var missing = await CocreClientGate.FindMissingAsync(
            new FakeWebLoanReader(),
            Form("""{"client":{"address":"1 St"},"loans":[]}"""),
            CancellationToken.None);

        Assert.Contains("Client ID is missing", missing);
    }

    [Fact]
    public async Task MissingCocreeItems_ListedByCode()
    {
        var reader = new FakeWebLoanReader();
        reader.PresentCocree.Remove("CCR03");
        reader.PresentCocree.Remove("CCR11");

        var missing = await CocreClientGate.FindMissingAsync(
            reader,
            Form("""{"client":{"cisId":"0880530879","address":"1 St"},"loans":[]}"""),
            CancellationToken.None);

        Assert.Contains(missing, m => m.Contains("COCRE CCR03"));
        Assert.Contains(missing, m => m.Contains("COCRE CCR11"));
        Assert.DoesNotContain(missing, m => m.Contains("COCRE CCR01"));
    }

    [Fact]
    public async Task MissingAddressAndContact_Blocks()
    {
        var reader = new FakeWebLoanReader
        {
            Address = null,
            Phone = null,
        };

        var missing = await CocreClientGate.FindMissingAsync(
            reader,
            Form("""{"client":{"cisId":"0880530879"},"loans":[]}"""),
            CancellationToken.None);

        Assert.Contains(missing, m => m.Contains("address is missing"));
        Assert.Contains(missing, m => m.Contains("contact number is missing"));
    }

    [Fact]
    public async Task FormAddressAndContact_SatisfyClientChecks()
    {
        var reader = new FakeWebLoanReader
        {
            Address = null,
            Phone = null,
        };

        var missing = await CocreClientGate.FindMissingAsync(
            reader,
            Form("""{"client":{"cisId":"0880530879","address":"1 Rizal St","contact":"09171234567"},"loans":[]}"""),
            CancellationToken.None);

        Assert.Empty(missing);
    }

    [Fact]
    public async Task CompleteProfileAndCocree_AllowSubmit()
    {
        var missing = await CocreClientGate.FindMissingAsync(
            new FakeWebLoanReader(),
            Form("""{"client":{"cisId":"0880530879"},"loans":[]}"""),
            CancellationToken.None);

        Assert.Empty(missing);
    }
}
