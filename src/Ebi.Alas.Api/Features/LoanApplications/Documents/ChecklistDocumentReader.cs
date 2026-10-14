using System.Data;
using System.Text.Json;
using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.Documents;

public sealed record ChecklistDocumentUpload(
    int? DocId,
    string? MiniStr,
    string? ContentType,
    DateTimeOffset? Created,
    string? UploadedBy);

/// <summary>
/// Resolves product document requirements against webloan + binary document store.
/// Matches the reference join: product codes × loan_data × check_list_all × doc_ref/docs.
/// </summary>
public interface IChecklistDocumentReader
{
    Task<IReadOnlyList<LoanChecklistDocumentDto>> GetChecklistDocumentsAsync(
        string loanNo,
        IReadOnlyCollection<string> idCodes,
        CancellationToken cancellationToken);

    Task<ChecklistDocumentUpload?> GetDocumentContentAsync(
        int docId,
        CancellationToken cancellationToken);
}

public sealed class ChecklistDocumentReader(
    AlasDbContext db,
    IWebLoanReader webLoan) : IChecklistDocumentReader
{
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

        return codes.Select(code =>
        {
            uploads.TryGetValue(code, out var upload);
            descriptionByCode.TryGetValue(code, out var description);
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
                UploadStatus: upload?.DocId is null ? "No uploaded documents" : "Uploaded");
        }).ToList();
    }

    public async Task<ChecklistDocumentUpload?> GetDocumentContentAsync(
        int docId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        // OPENQUERY cannot take parameters; docId is a validated int.
        command.CommandText = $@"
            SELECT TOP 1 d.mini_str, d.content_type, d.created, d.uploaded_by
            FROM OPENQUERY(BPB_BINARY_SERVER,
                'SELECT mini_str, content_type, created, uploaded_by
                 FROM bpb_binary.dbo.docs
                 WHERE doc_id = {docId}') d";
        command.CommandTimeout = 60;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ChecklistDocumentUpload(
            DocId: docId,
            MiniStr: reader.IsDBNull(0) ? null : reader.GetString(0),
            ContentType: reader.IsDBNull(1) ? null : reader.GetString(1),
            Created: reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            UploadedBy: reader.IsDBNull(3) ? null : reader.GetString(3));
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
            FROM OPENQUERY(BPB_BINARY_SERVER,
                'SELECT doc_id, check_list_item, cis_no, filename_str
                 FROM bpb_binary.dbo.doc_ref
                 WHERE cis_no = ''{sanitizedLoanNo}''
                   AND deleted IS NULL'
            ) dr
            LEFT JOIN OPENQUERY(BPB_BINARY_SERVER,
                'SELECT doc_id, mini_str, content_type, created, uploaded_by
                 FROM bpb_binary.dbo.docs
                 WHERE doc_id IN (
                     SELECT doc_id
                     FROM bpb_binary.dbo.doc_ref
                     WHERE cis_no = ''{sanitizedLoanNo}''
                       AND deleted IS NULL
                 )'
            ) d ON dr.doc_id = d.doc_id";
        command.CommandTimeout = 60;

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
}
