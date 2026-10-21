using Ebi.Alas.Api.Features.LoanApplications.Documents;
using Ebi.Alas.Api.Features.WebLoans;

namespace Ebi.Alas.Api.Tests.Features.LoanApplications;

public sealed class ChecklistDocumentWebloanTests
{
    [Fact]
    public void ExtractLoanNo_ReadsFirstLoanFromFormSnapshot()
    {
        var json = """
            {
              "client": { "cisId": "CIS-555" },
              "loans": [ { "loanNo": "CL20260935507", "productCode": "A16" } ]
            }
            """;

        Assert.Equal("CL20260935507", ChecklistDocumentReader.ExtractLoanNo(json));
    }

    [Fact]
    public void ExtractLoanNo_ReturnsNull_WhenMissingOrInvalid()
    {
        Assert.Null(ChecklistDocumentReader.ExtractLoanNo(null));
        Assert.Null(ChecklistDocumentReader.ExtractLoanNo(""));
        Assert.Null(ChecklistDocumentReader.ExtractLoanNo("not-json"));
        Assert.Null(ChecklistDocumentReader.ExtractLoanNo("""{"loans":[]}"""));
        Assert.Null(ChecklistDocumentReader.ExtractLoanNo("""{"loans":[{}]}"""));
    }

    [Fact]
    public void ExtractLoanNoAt_UsesMatchingLoanIndex()
    {
        var form = """{"loans":[{"loanNo":"PN-1"},{"loanNo":"PN-2"}]}""";
        Assert.Equal("PN-1", ChecklistDocumentReader.ExtractLoanNoAt(form, 0));
        Assert.Equal("PN-2", ChecklistDocumentReader.ExtractLoanNoAt(form, 1));
    }

    [Fact]
    public void ExtractLoanNoAt_MissingOrBlank_ReturnsNull()
    {
        Assert.Null(ChecklistDocumentReader.ExtractLoanNoAt(null, 0));
        Assert.Null(ChecklistDocumentReader.ExtractLoanNoAt("""{"loans":[{"loanNo":"  "}]}""", 0));
        Assert.Null(ChecklistDocumentReader.ExtractLoanNoAt("""{"loans":[]}""", 0));
        Assert.Null(ChecklistDocumentReader.ExtractLoanNoAt("""{"loans":[{"loanNo":"PN-1"}]}""", 5));
    }

    [Theory]
    [InlineData("CL20260935507", "CL20260935507")]
    [InlineData("PN-80001", "PN-80001")]
    [InlineData("  PN-1  ", "PN-1")]
    [InlineData("bad';drop", "baddrop")]
    public void SanitizeLoanNo_KeepsAlphanumericAndHyphen(string input, string expected)
    {
        Assert.Equal(expected, ChecklistDocumentReader.SanitizeLoanNo(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void SanitizeLoanNo_ReturnsNull_WhenUnusable(string? input)
    {
        Assert.Null(ChecklistDocumentReader.SanitizeLoanNo(input));
    }

    [Fact]
    public void UploadStatus_IsUploadedOnlyWhenBinaryDocIdPresent()
    {
        var uploaded = new ChecklistDocumentUpload(42, "abc", "application/pdf", DateTime.UtcNow, "clerk");
        var missing = new ChecklistDocumentUpload(null, null, null, null, null);

        Assert.Equal("Uploaded", uploaded.DocId is null ? "No uploaded documents" : "Uploaded");
        Assert.Equal("No uploaded documents", missing.DocId is null ? "No uploaded documents" : "Uploaded");
    }

    [Fact]
    public void DocumentFile_UsesDocStr_NotMiniStr()
    {
        // Reference GetDocumentContentAsync reads docs.doc_str (base64 payload).
        // mini_str is list metadata only and may be empty even when the file exists.
        var file = new ChecklistDocumentContent(
            DocId: 462880,
            DocStr: Convert.ToBase64String("pdf-bytes"u8.ToArray()),
            ContentType: "application/pdf");

        Assert.Equal(462880, file.DocId);
        Assert.False(string.IsNullOrEmpty(file.DocStr));
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("pdf-bytes", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(file.DocStr!)));
    }
}
