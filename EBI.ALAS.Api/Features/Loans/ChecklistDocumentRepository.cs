using System.Data;
using Microsoft.Data.SqlClient;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Implementation of IChecklistDocumentRepository using ADO.NET.
/// Queries BPB_BINARY_SERVER via OPENQUERY for checklist documents.
/// Uses DefaultConnection (ALASv2_DB) which has the linked server configured.
/// </summary>
public class ChecklistDocumentRepository : IChecklistDocumentRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ChecklistDocumentRepository> _logger;

    public ChecklistDocumentRepository(
        IConfiguration configuration,
        ILogger<ChecklistDocumentRepository> logger)
    {
        // Uses DefaultConnection (ALASv2_DB) which has the linked servers configured
        // OPENQUERY executes on the linked server through this connection
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string is not configured.");
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<List<LoanChecklistDocumentDto>> GetChecklistDocumentsAsync(
        string loanNo,
        CancellationToken cancellationToken = default)
    {
        // OPENQUERY requires literal strings - cannot use parameters inside it.
        // Sanitize loanNo to prevent SQL injection (only allow alphanumeric + hyphens)
        if (string.IsNullOrWhiteSpace(loanNo) || loanNo.Length > 50)
            return new List<LoanChecklistDocumentDto>();

        var sanitizedLoanNo = new string(loanNo.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (string.IsNullOrEmpty(sanitizedLoanNo))
            return new List<LoanChecklistDocumentDto>();

        // SQL query using OPENQUERY to access BPB_BINARY_SERVER
        // OPENQUERY requires literal string, so we concatenate the sanitized loanNo
        // Note: doc_str is binary content, so we don't select it in the list view
        var sql = $@"
            SELECT 
                ld.loan_no,
                ld.loan_product,
                lpc.IdCode AS id_code,
                cla.description AS checklist_description,
                dr.doc_id,
                d.mini_str,
                d.content_type,
                d.created,
                d.uploaded_by,
                CASE 
                    WHEN dr.doc_id IS NULL THEN 'No uploaded documents'
                    ELSE 'Uploaded'
                END AS upload_status
            FROM ALASv2_DB.dbo.LoanProductChecklist lpc
            INNER JOIN WEBLOAN_SERVER.webloan.dbo.loan_data ld
                ON lpc.LoanProduct = ld.loan_product
            LEFT JOIN WEBLOAN_SERVER.webloan.dbo.check_list_all cla
                ON lpc.IdCode = cla.id_code
            LEFT JOIN OPENQUERY(BPB_BINARY_SERVER,
                'SELECT doc_id, check_list_item, cis_no, filename_str
                 FROM bpb_binary.dbo.doc_ref
                 WHERE cis_no = ''{sanitizedLoanNo}''
                   AND deleted IS NULL'
            ) dr
                ON lpc.IdCode = dr.check_list_item
               AND dr.cis_no = ld.loan_no
            LEFT JOIN OPENQUERY(BPB_BINARY_SERVER,
                'SELECT doc_id, mini_str, content_type, created, uploaded_by
                 FROM bpb_binary.dbo.docs
                 WHERE doc_id IN (
                     SELECT doc_id 
                     FROM bpb_binary.dbo.doc_ref
                     WHERE cis_no = ''{sanitizedLoanNo}''
                       AND deleted IS NULL
                 )'
            ) d
                ON dr.doc_id = d.doc_id
            WHERE ld.loan_no = @loanNo;";

        var results = new List<LoanChecklistDocumentDto>();

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = 60; // OPENQUERY can be slow
            command.Parameters.Add("@loanNo", SqlDbType.VarChar, 50).Value = sanitizedLoanNo;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new LoanChecklistDocumentDto
                {
                    LoanNo = reader.GetString(reader.GetOrdinal("loan_no")),
                    LoanProduct = reader.GetString(reader.GetOrdinal("loan_product")),
                    IdCode = reader.GetString(reader.GetOrdinal("id_code")),
                    ChecklistDescription = reader.IsDBNull(reader.GetOrdinal("checklist_description"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("checklist_description")),
                    DocId = reader.IsDBNull(reader.GetOrdinal("doc_id"))
                        ? null
                        : Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("doc_id"))),
                    DocStr = null, // Binary content, not fetched in list view
                    MiniStr = reader.IsDBNull(reader.GetOrdinal("mini_str"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("mini_str")),
                    ContentType = reader.IsDBNull(reader.GetOrdinal("content_type"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("content_type")),
                    Created = reader.IsDBNull(reader.GetOrdinal("created"))
                        ? null
                        : reader.GetDateTime(reader.GetOrdinal("created")),
                    UploadedBy = reader.IsDBNull(reader.GetOrdinal("uploaded_by"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("uploaded_by")),
                    UploadStatus = reader.GetString(reader.GetOrdinal("upload_status"))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying checklist documents for loan {LoanNo}", loanNo);
            throw;
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<DocumentContentDto?> GetDocumentContentAsync(
        int docId,
        CancellationToken cancellationToken = default)
    {
        // doc_str is the binary content column
        var sql = $@"
            SELECT TOP 1
                d.doc_str,
                d.content_type
            FROM OPENQUERY(BPB_BINARY_SERVER,
                'SELECT doc_str, content_type
                 FROM bpb_binary.dbo.docs
                 WHERE doc_id = {docId}
            ') d;";

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = 60;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var contentType = reader.IsDBNull(reader.GetOrdinal("content_type"))
                    ? "application/octet-stream"
                    : reader.GetString(reader.GetOrdinal("content_type"));

                byte[] content;
                if (!reader.IsDBNull(reader.GetOrdinal("doc_str")))
                {
                    var base64String = reader.GetString(reader.GetOrdinal("doc_str"));
                    try
                    {
                        content = Convert.FromBase64String(base64String);
                    }
                    catch (FormatException)
                    {
                        _logger.LogWarning("doc_str for docId {DocId} is not valid base64", docId);
                        content = Array.Empty<byte>();
                    }
                }
                else
                {
                    content = Array.Empty<byte>();
                }

                return new DocumentContentDto
                {
                    Content = content,
                    ContentType = contentType,
                    FileName = $"document-{docId}.{GetFileExtension(contentType)}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching document content for docId {DocId}", docId);
            throw;
        }

        return null;
    }

    private static string GetFileExtension(string contentType)
    {
        return contentType?.ToLowerInvariant() switch
        {
            "application/pdf" => "pdf",
            "image/jpeg" or "image/jpg" => "jpg",
            "image/png" => "png",
            "image/gif" => "gif",
            "image/bmp" => "bmp",
            "image/tiff" => "tiff",
            "application/msword" => "doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            "application/vnd.ms-excel" => "xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
            "text/plain" => "txt",
            "text/html" => "html",
            _ => "bin"
        };
    }

    /// <inheritdoc/>
    public async Task<string?> GetDocumentLoanNoAsync(
        int docId,
        CancellationToken cancellationToken = default)
    {
        // Resolve docId → cis_no (loan number) from doc_ref.
        // This is used to verify the caller has access to the loan before serving content.
        // NOTE: OPENQUERY executes the string on the linked server, so we must interpolate
        // the value into the string literal (like the other two methods in this class).
        // docId is an int — safe from SQL injection.
        var sql = $@"
            SELECT TOP 1 cis_no
            FROM OPENQUERY(BPB_BINARY_SERVER,
                'SELECT cis_no
                 FROM bpb_binary.dbo.doc_ref
                 WHERE doc_id = {docId}
                   AND deleted IS NULL'
            );";

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = 30;

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result as string;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving loan number for docId {DocId}", docId);
            return null;
        }
    }
}
