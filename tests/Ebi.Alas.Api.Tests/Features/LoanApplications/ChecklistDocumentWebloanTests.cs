using Ebi.Alas.Api.Features.LoanApplications.Documents;
using Ebi.Alas.Api.Features.WebLoans;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class ChecklistDocumentWebloanTests
{
    [Fact]
    public void ExtractCisId_ReadsClientCisIdFromFormSnapshot()
    {
        var json = """{"client":{"cisId":"CIS-555","firstName":"Maria"},"loans":[]}""";

        Assert.Equal("CIS-555", ChecklistDocumentEndpoints.ExtractCisId(json));
    }

    [Fact]
    public void ExtractCisId_ReturnsNull_WhenMissingOrInvalid()
    {
        Assert.Null(ChecklistDocumentEndpoints.ExtractCisId(null));
        Assert.Null(ChecklistDocumentEndpoints.ExtractCisId(""));
        Assert.Null(ChecklistDocumentEndpoints.ExtractCisId("not-json"));
        Assert.Null(ChecklistDocumentEndpoints.ExtractCisId("""{"client":{}}"""));
    }

    [Fact]
    public void WebloanChecklistRow_UploadStatus_IsUploadedOnlyWhenSubmitted()
    {
        var uploaded = new WebLoanChecklistRow("A1004", "Approved ID", DateTime.UtcNow);
        var pending = new WebLoanChecklistRow("PIC02", "Photo ID", null);

        Assert.True(uploaded.Submitted is not null);
        Assert.True(pending.Submitted is null);
    }
}
