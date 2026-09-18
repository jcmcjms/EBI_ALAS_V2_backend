using OfficeOpenXml;
using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.AuditLogs;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public interface ILoanProductImportService
{
    Task<byte[]> ExportAsync(ExportLoanProductsParameters parameters, CancellationToken ct);
    Task<byte[]> GenerateTemplateAsync(CancellationToken ct);
    Task<LoanProductImportResult> ImportAsync(Stream file, int operatorId, string operatorName, CancellationToken ct);
}

public class LoanProductImportService : ILoanProductImportService
{
    private readonly AppDbContext _context;
    private readonly ITimeProvider _timeProvider;
    private readonly IAuditLogService _auditLogService;

    // Hard ceiling from LoanProductService — no product may offer more
    // than 7 years + 2 months grace period (2,617 days).
    private const int AbsoluteMaxTermDays = 2617;

    public LoanProductImportService(
        AppDbContext context,
        ITimeProvider timeProvider,
        IAuditLogService auditLogService)
    {
        _context = context;
        _timeProvider = timeProvider;
        _auditLogService = auditLogService;
    }

    public async Task<byte[]> ExportAsync(ExportLoanProductsParameters parameters, CancellationToken ct)
    {
        var query = _context.LoanProducts.AsNoTracking();

        if (parameters.IncludeRetired == false)
            query = query.Where(p => !p.IsRetired);

        var products = await query.OrderBy(p => p.Code).ToListAsync(ct);

        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Loan Products");

        var headers = new[] {
            "Code", "Description", "Min Amount", "Max Amount",
            "Min Term (Days)", "Max Term (Days)", "Notarial Fee",
            "Doc Stamp Fee", "Insurance Fee", "Advance Interest Rate",
            "Application Charge Rate", "Amortization Mode",
            "Charge Advance Interest", "Is Retired", "Last Updated"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cells[1, i + 1].Value = headers[i];
            worksheet.Cells[1, i + 1].Style.Font.Bold = true;
        }

        for (int row = 0; row < products.Count; row++)
        {
            var p = products[row];
            var excelRow = row + 2;

            worksheet.Cells[excelRow, 1].Value = p.Code;
            worksheet.Cells[excelRow, 2].Value = p.Description;
            worksheet.Cells[excelRow, 3].Value = p.MinAmount;
            worksheet.Cells[excelRow, 4].Value = p.MaxAmount;
            worksheet.Cells[excelRow, 5].Value = p.MinTermDays;
            worksheet.Cells[excelRow, 6].Value = p.MaxTermDays;
            worksheet.Cells[excelRow, 7].Value = p.NotarialFee;
            worksheet.Cells[excelRow, 8].Value = p.DocStampFee;
            worksheet.Cells[excelRow, 9].Value = p.InsuranceFee;
            worksheet.Cells[excelRow, 10].Value = p.AdvanceInterestRate;
            worksheet.Cells[excelRow, 11].Value = p.ApplicationChargeRate;
            worksheet.Cells[excelRow, 12].Value = p.AmortizationMode;
            worksheet.Cells[excelRow, 13].Value = p.ChargeAdvanceInterest ? "Yes" : "No";
            worksheet.Cells[excelRow, 14].Value = p.IsRetired ? "Yes" : "No";
            worksheet.Cells[excelRow, 15].Value = p.UpdatedDate.ToString("yyyy-MM-dd HH:mm:ss");
        }

        worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
        return await package.GetAsByteArrayAsync(ct);
    }

    public async Task<byte[]> GenerateTemplateAsync(CancellationToken ct)
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Template");

        var headers = new[] {
            "Code *", "Description *", "Min Amount *", "Max Amount *",
            "Min Term Days *", "Max Term Days *", "Notarial Fee",
            "Doc Stamp Fee", "Insurance Fee", "Advance Interest Rate",
            "Application Charge Rate", "Amortization Mode",
            "Charge Advance Interest", "Is Retired"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cells[1, i + 1].Value = headers[i];
            worksheet.Cells[1, i + 1].Style.Font.Bold = true;
        }

        // Example rows
        worksheet.Cells[2, 1].Value = "A16";
        worksheet.Cells[2, 2].Value = "Personal Loan";
        worksheet.Cells[2, 3].Value = 10000;
        worksheet.Cells[2, 4].Value = 500000;
        worksheet.Cells[2, 5].Value = 180;
        worksheet.Cells[2, 6].Value = 2555;
        worksheet.Cells[2, 7].Value = 500;
        worksheet.Cells[2, 8].Value = 300;
        worksheet.Cells[2, 9].Value = 1000;
        worksheet.Cells[2, 10].Value = 0.07;
        worksheet.Cells[2, 11].Value = 0.06;
        worksheet.Cells[2, 12].Value = "DIM";
        worksheet.Cells[2, 13].Value = "No";
        worksheet.Cells[2, 14].Value = "No";

        worksheet.Cells[3, 1].Value = "C35";
        worksheet.Cells[3, 2].Value = "Multi-Purpose Loan";
        worksheet.Cells[3, 3].Value = 20000;
        worksheet.Cells[3, 4].Value = 1000000;
        worksheet.Cells[3, 5].Value = 365;
        worksheet.Cells[3, 6].Value = 2555;
        worksheet.Cells[3, 7].Value = 1000;
        worksheet.Cells[3, 8].Value = 500;
        worksheet.Cells[3, 9].Value = 2000;
        worksheet.Cells[3, 10].Value = 0.18;
        worksheet.Cells[3, 11].Value = 0.075;
        worksheet.Cells[3, 12].Value = "MIC";
        worksheet.Cells[3, 13].Value = "Yes";
        worksheet.Cells[3, 14].Value = "No";

        // Instructions sheet
        var instructions = package.Workbook.Worksheets.Add("Instructions");
        instructions.Cells[1, 1].Value = "Loan Product Import Instructions";
        instructions.Cells[1, 1].Style.Font.Size = 14;
        instructions.Cells[1, 1].Style.Font.Bold = true;

        var instructionText = new[]
        {
            "Required fields are marked with *",
            "Code: Unique product identifier (e.g., A16, C35). Used as primary key.",
            "Description: Human-readable product name.",
            "Min/Max Amount: Eligibility bounds in PHP. Max must be >= Min.",
            "Min/Max Term Days: Term bounds in days. Max must be >= Min and <= 2,617 (7 years).",
            "Fees: Flat fees in PHP (Notarial, Doc Stamp, Insurance). Cannot be negative.",
            "Advance Interest Rate: Decimal (0.12 = 12% p.a.). Must be 0-1 if provided. Defaults to 0.",
            "Application Charge Rate: Decimal (0.06 = 6%). Must be 0-1 if provided. Defaults to 0.",
            "Amortization Mode: DIM (diminishing) or MIC (minimum installment check). Defaults to DIM.",
            "Charge Advance Interest: Yes/No. True for add-on products. Defaults to No.",
            "Is Retired: Yes/No. Defaults to No if omitted.",
            "",
            "Import behavior: Upsert semantics — existing codes are updated, new codes are created.",
            "Sync-owned fields (Description, IsRetired) from imported products will be overwritten on next sync if the code exists in webloan.",
            "Checklist documents are NOT imported — use the product edit form to configure document requirements.",
            "",
            "Row numbers in validation errors are the Excel row numbers (header = row 1).",
            "Completely empty rows are ignored — clear a row's contents to exclude it.",
            "When updating an existing code, blank cells keep the current value; blank required cells only fail rows for NEW codes.",
            "Is Retired is ignored on update — retirement is owned by the webloan sync."
        };

        for (int i = 0; i < instructionText.Length; i++)
        {
            instructions.Cells[i + 2, 1].Value = instructionText[i];
        }

        worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
        instructions.Cells[instructions.Dimension.Address].AutoFitColumns();

        return await package.GetAsByteArrayAsync(ct);
    }

    public async Task<LoanProductImportResult> ImportAsync(
        Stream file, int operatorId, string operatorName, CancellationToken ct)
    {
        var errors = new List<LoanProductImportValidationError>();
        var created = 0;
        var updated = 0;
        var totalRows = 0;

        using var package = new ExcelPackage(file);
        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
        if (worksheet?.Dimension == null)
        {
            errors.Add(new LoanProductImportValidationError(1, "File", "Excel file is empty or corrupted."));
            return new LoanProductImportResult(0, 0, 0, 1, errors);
        }

        var now = _timeProvider.UtcNow;

        for (var excelRow = 2; excelRow <= worksheet.Dimension.Rows; excelRow++)
        {
            // Excel's Dimension spans rows that were formatted or cleared but
            // never deleted. They carry no data and must never surface as
            // "required" errors — this was the false-positive source.
            if (IsBlankRow(worksheet, excelRow)) continue;

            totalRows++;
            var rowErrors = new List<LoanProductImportValidationError>();
            // Row numbers in the report are the ACTUAL Excel row (header = 1),
            // so ops can jump straight to the offending line.
            void Fail(string field, string error) =>
                rowErrors.Add(new LoanProductImportValidationError(excelRow, field, error));

            var code = GetCellString(worksheet, excelRow, 1);
            var description = GetCellString(worksheet, excelRow, 2);
            var minAmount = GetCellDecimal(worksheet, excelRow, 3);
            var maxAmount = GetCellDecimal(worksheet, excelRow, 4);
            var minTermDays = GetCellInt(worksheet, excelRow, 5);
            var maxTermDays = GetCellInt(worksheet, excelRow, 6);
            var notarialFee = GetCellDecimal(worksheet, excelRow, 7);
            var docStampFee = GetCellDecimal(worksheet, excelRow, 8);
            var insuranceFee = GetCellDecimal(worksheet, excelRow, 9);
            var advanceInterestRate = GetCellDecimal(worksheet, excelRow, 10);
            var applicationChargeRate = GetCellDecimal(worksheet, excelRow, 11);
            var amortizationMode = GetCellString(worksheet, excelRow, 12);
            var chargeAdvanceInterestRaw = GetCellString(worksheet, excelRow, 13);
            var chargeAdvanceInterest = ParseYesNo(chargeAdvanceInterestRaw);
            var isRetired = ParseYesNo(GetCellString(worksheet, excelRow, 14));

            // Tracked lookup: updates mutate this instance and save at the end.
            var existing = string.IsNullOrWhiteSpace(code)
                ? null
                : await _context.LoanProducts.FirstOrDefaultAsync(p => p.Code == code, ct);
            var isUpdate = existing is not null;

            // ── Key + description ─────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(code))
                Fail("Code", "Code is required.");
            else if (code.Length > 50)
                Fail("Code", "Code must be 50 characters or fewer.");

            if (!isUpdate && string.IsNullOrWhiteSpace(description))
                Fail("Description", "Description is required for new products.");

            // ── Bounds: required on create; on update blank = keep existing,
            //    cross-field checks run against the effective (merged) pair. ──
            if (!isUpdate)
            {
                if (minAmount == null) Fail("Min Amount", "Min amount is required for new products.");
                if (maxAmount == null) Fail("Max Amount", "Max amount is required for new products.");
                if (minTermDays == null) Fail("Min Term Days", "Min term is required for new products.");
                if (maxTermDays == null) Fail("Max Term Days", "Max term is required for new products.");
                if (advanceInterestRate == null) Fail("Advance Interest Rate", "Advance interest rate is required for new products.");
                if (applicationChargeRate == null) Fail("Application Charge Rate", "Application charge rate is required for new products.");
                if (string.IsNullOrWhiteSpace(amortizationMode)) Fail("Amortization Mode", "Amortization mode is required for new products.");
                if (chargeAdvanceInterest == null) Fail("Charge Advance Interest", "Yes/No is required for new products.");
            }

            if (minAmount < 0) Fail("Min Amount", "Min amount cannot be negative.");
            if (maxAmount < 0) Fail("Max Amount", "Max amount cannot be negative.");
            if (minTermDays < 0) Fail("Min Term Days", "Min term cannot be negative.");
            if (notarialFee < 0 || docStampFee < 0 || insuranceFee < 0)
                Fail("Fees", "Fees cannot be negative.");
            if (advanceInterestRate is < 0 or > 1)
                Fail("Advance Interest Rate", "Rate must be between 0 and 1 (e.g. 0.12 for 12%).");
            if (applicationChargeRate is < 0 or > 1)
                Fail("Application Charge Rate", "Rate must be between 0 and 1 (e.g. 0.06 for 6%).");
            if (amortizationMode is not null && amortizationMode != "DIM" && amortizationMode != "MIC")
                Fail("Amortization Mode", "Must be DIM or MIC.");
            if (chargeAdvanceInterestRaw != null && chargeAdvanceInterest == null)
                Fail("Charge Advance Interest", "Must be Yes or No.");

            var effMinAmount = minAmount ?? existing?.MinAmount ?? 0;
            var effMaxAmount = maxAmount ?? existing?.MaxAmount ?? 0;
            var effMinTerm = minTermDays ?? existing?.MinTermDays ?? 0;
            var effMaxTerm = maxTermDays ?? existing?.MaxTermDays ?? 0;

            if (effMaxAmount < effMinAmount)
                Fail("Max Amount", $"Max amount ({effMaxAmount:N2}) must be >= min amount ({effMinAmount:N2}).");
            if (effMaxTerm < effMinTerm)
                Fail("Max Term Days", $"Max term ({effMaxTerm}) must be >= min term ({effMinTerm}).");
            if (effMaxTerm > AbsoluteMaxTermDays)
                Fail("Max Term Days", $"Max term ({effMaxTerm}) cannot exceed {AbsoluteMaxTermDays} days.");

            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors);
                continue;
            }

            if (isUpdate)
            {
                // Partial update: only cells the file actually provided change.
                // Blank never silently zeroes a rate or fee on a live product.
                if (description != null) existing!.Description = description;
                if (minAmount.HasValue) existing!.MinAmount = minAmount.Value;
                if (maxAmount.HasValue) existing!.MaxAmount = maxAmount.Value;
                if (minTermDays.HasValue) existing!.MinTermDays = minTermDays.Value;
                if (maxTermDays.HasValue) existing!.MaxTermDays = maxTermDays.Value;
                if (notarialFee.HasValue) existing!.NotarialFee = notarialFee.Value;
                if (docStampFee.HasValue) existing!.DocStampFee = docStampFee.Value;
                if (insuranceFee.HasValue) existing!.InsuranceFee = insuranceFee.Value;
                if (advanceInterestRate.HasValue) existing!.AdvanceInterestRate = advanceInterestRate.Value;
                if (applicationChargeRate.HasValue) existing!.ApplicationChargeRate = applicationChargeRate.Value;
                if (amortizationMode != null) existing!.AmortizationMode = amortizationMode;
                if (chargeAdvanceInterest.HasValue) existing!.ChargeAdvanceInterest = chargeAdvanceInterest.Value;
                // IsRetired deliberately ignored on update — sync-owned field;
                // an import must never un-retire a product behind webloan's back.
                existing!.UpdatedDate = now;
                existing.UpdatedById = operatorId;
                updated++;

                await _auditLogService.LogAsync(
                    operatorId, operatorName, "Import", "LoanProduct", code!, existing.Description,
                    "Loan product policy fields updated via batch import");
            }
            else
            {
                _context.LoanProducts.Add(new LoanProduct
                {
                    Code = code!,
                    Description = description!,
                    MinAmount = minAmount!.Value,
                    MaxAmount = maxAmount!.Value,
                    MinTermDays = minTermDays!.Value,
                    MaxTermDays = maxTermDays!.Value,
                    NotarialFee = notarialFee ?? 0,
                    DocStampFee = docStampFee ?? 0,
                    InsuranceFee = insuranceFee ?? 0,
                    AdvanceInterestRate = advanceInterestRate!.Value,
                    ApplicationChargeRate = applicationChargeRate!.Value,
                    AmortizationMode = amortizationMode!,
                    ChargeAdvanceInterest = chargeAdvanceInterest!.Value,
                    IsRetired = isRetired ?? false,
                    LastSyncedAt = DateTime.MinValue, // never synced from webloan
                    UpdatedDate = now,
                    UpdatedById = operatorId,
                });
                created++;

                await _auditLogService.LogAsync(
                    operatorId, operatorName, "Import", "LoanProduct", code!, description!,
                    "Loan product created via batch import");
            }
        }

        await _context.SaveChangesAsync(ct);
        return new LoanProductImportResult(totalRows, created, updated, errors.Count, errors);
    }

    /** True when every cell in the row's used range is null/whitespace. */
    private static bool IsBlankRow(ExcelWorksheet worksheet, int row)
    {
        var endCol = worksheet.Dimension?.End.Column ?? 1;
        for (var col = 1; col <= endCol; col++)
        {
            var value = worksheet.Cells[row, col].Value;
            if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                return false;
        }
        return true;
    }

    // ─── Cell parsing helpers ───────────────────────────────────────────────
    // EPPlus returns cells as `object?` — these helpers convert to typed
    // values with null semantics so missing cells produce validation errors
    // rather than format exceptions.

    private static string? GetCellString(ExcelWorksheet worksheet, int row, int col)
    {
        var value = worksheet.Cells[row, col].Value?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static decimal? GetCellDecimal(ExcelWorksheet worksheet, int row, int col)
    {
        var value = worksheet.Cells[row, col].Value;
        if (value == null) return null;
        if (value is decimal d) return d;
        if (decimal.TryParse(value.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static int? GetCellInt(ExcelWorksheet worksheet, int row, int col)
    {
        var value = worksheet.Cells[row, col].Value;
        if (value == null) return null;
        if (value is int i) return i;
        if (value is double dbl) return (int)dbl;
        if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static bool? ParseYesNo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().ToLowerInvariant();
        if (v is "yes" or "true" or "1" or "y") return true;
        if (v is "no" or "false" or "0" or "n") return false;
        return null;
    }
}
