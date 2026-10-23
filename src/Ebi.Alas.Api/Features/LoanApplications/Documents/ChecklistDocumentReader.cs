using System.Data;
using System.Text.Json;
using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Features.LoanApplications.Documents;

public sealed record ChecklistDocumentUpload(
    int? DocId,
    string? MiniStr,
    string? ContentType,
    DateTimeOffset? Created,
    string? UploadedBy);

public sealed record ChecklistDocumentContent(
    int DocId,
    string? DocStr,
    string ContentType);

public interface IChecklistDocumentReader
{
    Task<IReadOnlyList<LoanChecklistDocumentDto>> GetChecklistDocumentsAsync(
        string loanNo,
        IReadOnlyCollection<string> idCodes,
        CancellationToken cancellationToken);

    Task<ChecklistDocumentContent?> GetDocumentFileAsync(
        int docId,
        CancellationToken cancellationToken);
}

public sealed class ChecklistDocumentReader(
    AlasDbContext db,
    IWebLoanReader webLoan,
    IOptions<WebLoanOptions> webLoanOptions) : IChecklistDocumentReader
{
    private readonly string _binaryDb = webLoanOptions.Value.BinaryDatabase is { Length: > 0 } name
        ? name
        : "bpb_binary";

    private string DocsTable() => $"{_binaryDb}.dbo.docs";

    private string DocRefTable() => $"{_binaryDb}.dbo.doc_ref";

    public async Task<IReadOnlyList<LoanChecklistDocumentDto>> GetChecklistDocumentsAsync(
        string loanNo,
        IReadOnlyCollection<string> idCodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idCodes);
        var sanitizedLoanNo = SanitizeLoanNo(loanNo);
        if (sanitizedLoanNo is null || idCodes.Count == 0)
        {
            return [];
        }

        // Reference INNER JOINs loan_data — no webloan loan means no checklist rows.
        var loan = await webLoan.GetLoanByNoAsync(sanitizedLoanNo, cancellationToken);
        if (loan is null)
        {
            return [];
        }

        var codes = idCodes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0)
        {
            return [];
        }

        var catalog = await webLoan.GetChecklistCatalogAsync(codes, cancellationToken);
        var descriptionByCode = catalog
            .GroupBy(c => c.IdCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Description, StringComparer.OrdinalIgnoreCase);

        var uploads = await LoadUploadsAsync(sanitizedLoanNo, codes, cancellationToken);
        // First amortization implies the paper requirements were already filed in WebLoan.
        var amortized = await webLoan.HasFirstAmortizationAsync(sanitizedLoanNo, cancellationToken);

        return codes.Select(code =>
        {
            uploads.TryGetValue(code, out var upload);
            descriptionByCode.TryGetValue(code, out var description);
            var uploadStatus = upload?.DocId is not null
                ? "Uploaded"
                : amortized
                    ? "Submitted"
                    : "No uploaded documents";
            return new LoanChecklistDocumentDto(
                LoanNo: loan.LoanNo,
                LoanProduct: loan.LoanProduct,
                IdCode: code,
                ChecklistDescription: description,
                DocId: upload?.DocId,
                DocStr: null,
                MiniStr: upload?.MiniStr,
                ContentType: upload?.ContentType,
                Created: upload?.Created,
                UploadedBy: upload?.UploadedBy,
                UploadStatus: uploadStatus);
        }).ToList();
    }

    public async Task<ChecklistDocumentContent?> GetDocumentFileAsync(
        int docId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT TOP (1) doc_str, content_type
            FROM {DocsTable()}
            WHERE doc_id = @docId";
        command.CommandTimeout = 60;

        var pDocId = command.CreateParameter();
        pDocId.ParameterName = "@docId";
        pDocId.Value = docId;
        command.Parameters.Add(pDocId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var contentType = reader.IsDBNull(1) ? "application/octet-stream" : reader.GetString(1);
        return new ChecklistDocumentContent(
            DocId: docId,
            DocStr: reader.IsDBNull(0) ? null : reader.GetString(0),
            ContentType: contentType);
    }

    private async Task<Dictionary<string, ChecklistDocumentUpload>> LoadUploadsAsync(
        string sanitizedLoanNo,
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // Same sources as the reference: bpb_binary doc_ref + docs, keyed by loan number as cis_no.
        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT
                dr.check_list_item,
                dr.doc_id,
                d.mini_str,
                d.content_type,
                d.created,
                d.uploaded_by
            FROM {DocRefTable()} AS dr
            LEFT JOIN {DocsTable()} AS d ON dr.doc_id = d.doc_id
            WHERE dr.cis_no = @loanNo
              AND dr.deleted IS NULL";
        command.CommandTimeout = 60;

        var pLoanNo = command.CreateParameter();
        pLoanNo.ParameterName = "@loanNo";
        pLoanNo.Value = sanitizedLoanNo;
        command.Parameters.Add(pLoanNo);

        var byCode = new Dictionary<string, ChecklistDocumentUpload>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = reader.GetString(0);
            if (!codes.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            byCode[item] = new ChecklistDocumentUpload(
                DocId: reader.IsDBNull(1) ? null : Convert.ToInt32(reader.GetInt64(1)),
                MiniStr: reader.IsDBNull(2) ? null : reader.GetString(2),
                ContentType: reader.IsDBNull(3) ? null : reader.GetString(3),
                Created: reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                UploadedBy: reader.IsDBNull(5) ? null : reader.GetString(5));
        }

        return byCode;
    }

    public static string? SanitizeLoanNo(string? loanNo)
    {
        if (string.IsNullOrWhiteSpace(loanNo) || loanNo.Length > 50)
        {
            return null;
        }

        var sanitized = new string(loanNo.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        return string.IsNullOrEmpty(sanitized) ? null : sanitized;
    }

    public static string? ExtractLoanNo(string? formJson)
    {
        if (string.IsNullOrWhiteSpace(formJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(formJson);
            if (doc.RootElement.TryGetProperty("loans", out var loans)
                && loans.ValueKind == JsonValueKind.Array
                && loans.GetArrayLength() > 0)
            {
                var first = loans[0];
                if (first.ValueKind == JsonValueKind.Object
                    && first.TryGetProperty("loanNo", out var loanNo)
                    && loanNo.ValueKind == JsonValueKind.String)
                {
                    var value = loanNo.GetString()?.Trim();
                    return string.IsNullOrEmpty(value) ? null : value;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static string? ExtractLoanNoAt(string? formJson, int loanIndex)
    {
        if (string.IsNullOrWhiteSpace(formJson) || loanIndex < 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(formJson);
            if (!TryGetLoanAt(doc.RootElement, loanIndex, out var loan))
            {
                return null;
            }

            if (!loan.TryGetProperty("loanNo", out var loanNo)
                || loanNo.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = loanNo.GetString()?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Form creationTypeCode: 0=New Loan, 1=Reloan, 6=Additional Loan.
    /// Reloan/Additional use the Reloan document matrix; other codes fall back.
    /// </summary>
    public static LoanApplications.Domain.LoanType ResolveLoanTypeAt(
        string? formJson,
        int loanIndex,
        LoanApplications.Domain.LoanType fallback)
    {
        if (string.IsNullOrWhiteSpace(formJson) || loanIndex < 0)
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(formJson);
            if (!TryGetLoanAt(doc.RootElement, loanIndex, out var loan)
                || !loan.TryGetProperty("creationTypeCode", out var code)
                || code.ValueKind != JsonValueKind.Number
                || !code.TryGetInt32(out var creationTypeCode))
            {
                return fallback;
            }

            return creationTypeCode switch
            {
                0 => LoanApplications.Domain.LoanType.New,
                1 or 6 => LoanApplications.Domain.LoanType.Reloan,
                _ => fallback
            };
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static bool TryGetLoanAt(JsonElement root, int loanIndex, out JsonElement loan)
    {
        loan = default;
        if (!root.TryGetProperty("loans", out var loans)
            || loans.ValueKind != JsonValueKind.Array
            || loanIndex >= loans.GetArrayLength())
        {
            return false;
        }

        loan = loans[loanIndex];
        return loan.ValueKind == JsonValueKind.Object;
    }
}
