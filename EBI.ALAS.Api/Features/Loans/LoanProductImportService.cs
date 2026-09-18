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
    Task<LoanProductImportResult> ImportAsync(Stream file, int operatorId, CancellationToken ct);
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
            "Checklist documents are NOT imported — use the product edit form to configure document requirements."
        };

        for (int i = 0; i < instructionText.Length; i++)
        {
            instructions.Cells[i + 2, 1].Value = instructionText[i];
        }

        worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
        instructions.Cells[instructions.Dimension.Address].AutoFitColumns();

        return await package.GetAsByteArrayAsync(ct);
    }

    public async Task<LoanProductImportResult> ImportAsync(Stream file, int operatorId, CancellationToken ct)
    {
        var errors = new List<LoanProductImportValidationError>();
        var created = 0;
        var updated = 0;
        var totalRows = 0;

        using var package = new ExcelPackage(file);
        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
        if (worksheet == null || worksheet.Dimension == null)
        {
            errors.Add(new LoanProductImportValidationError(0, "File", "Excel file is empty or corrupted"));
            return new LoanProductImportResult(0, 0, 0, 0, errors);
        }

        totalRows = worksheet.Dimension.Rows - 1;
        var now = _timeProvider.UtcNow;

        for (int excelRow = 2; excelRow <= worksheet.Dimension.Rows; excelRow++)
        {
            var rowNumber = excelRow - 1;
            var code = GetCellString(worksheet, excelRow, 1);
            var description = GetCellString(worksheet, excelRow, 2);
            var minAmount = GetCellDecimal(worksheet, excelRow, 3);
            var maxAmount = GetCellDecimal(worksheet, excelRow, 4);
            var minTermDays = GetCellInt(worksheet, excelRow, 5);
            var maxTermDays = GetCellInt(worksheet, excelRow, 6);
            var notarialFee = GetCellDecimal(worksheet, excelRow, 7) ?? 0;
            var docStampFee = GetCellDecimal(worksheet, excelRow, 8) ?? 0;
            var insuranceFee = GetCellDecimal(worksheet, excelRow, 9) ?? 0;
            var advanceInterestRate = GetCellDecimal(worksheet, excelRow, 10);
            var applicationChargeRate = GetCellDecimal(worksheet, excelRow, 11);
            var amortizationMode = GetCellString(worksheet, excelRow, 12);
            var chargeAdvanceInterest = ParseYesNo(GetCellString(worksheet, excelRow, 13));
            var isRetired = ParseYesNo(GetCellString(worksheet, excelRow, 14)) ?? false;

            // ── Validation (mirrors ValidatePolicyFields in LoanProductService) ──
            if (string.IsNullOrWhiteSpace(code))
                errors.Add(new LoanProductImportValidationError(rowNumber, "Code", "Code is required"));
            else if (code.Length > 50)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Code", "Code must be <= 50 characters"));

            if (string.IsNullOrWhiteSpace(description))
                errors.Add(new LoanProductImportValidationError(rowNumber, "Description", "Description is required"));

            if (minAmount == null)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Min Amount", "Min amount is required"));
            else if (minAmount < 0)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Min Amount", "Min amount cannot be negative"));

            if (maxAmount == null)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Max Amount", "Max amount is required"));
            else if (minAmount.HasValue && maxAmount < minAmount)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Max Amount", $"Max amount ({maxAmount}) must be >= Min amount ({minAmount})"));

            if (minTermDays == null)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Min Term Days", "Min term is required"));
            else if (minTermDays < 0)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Min Term Days", "Min term cannot be negative"));

            if (maxTermDays == null)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Max Term Days", "Max term is required"));
            else if (minTermDays.HasValue && maxTermDays < minTermDays)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Max Term Days", $"Max term ({maxTermDays}) must be >= Min term ({minTermDays})"));
            else if (maxTermDays > AbsoluteMaxTermDays)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Max Term Days", $"Max term ({maxTermDays}) cannot exceed {AbsoluteMaxTermDays} days"));

            if (notarialFee < 0 || docStampFee < 0 || insuranceFee < 0)
                errors.Add(new LoanProductImportValidationError(rowNumber, "Fees", "Fees cannot be negative"));

            if (advanceInterestRate.HasValue && (advanceInterestRate < 0 || advanceInterestRate > 1))
                errors.Add(new LoanProductImportValidationError(rowNumber, "Advance Interest Rate", "Rate must be 0-1 (e.g., 0.12 for 12%)"));

            if (applicationChargeRate.HasValue && (applicationChargeRate < 0 || applicationChargeRate > 1))
                errors.Add(new LoanProductImportValidationError(rowNumber, "Application Charge Rate", "Rate must be 0-1 (e.g., 0.06 for 6%)"));

            if (!string.IsNullOrWhiteSpace(amortizationMode) && amortizationMode != "DIM" && amortizationMode != "MIC")
                errors.Add(new LoanProductImportValidationError(rowNumber, "Amortization Mode", "Must be DIM or MIC"));

            // Skip this row if there are validation errors
            if (errors.Any(e => e.RowNumber == rowNumber))
                continue;

            // ── Upsert ──────────────────────────────────────────────────────
            // Apply defaults for optional fields
            var effectiveAdvanceInterestRate = advanceInterestRate ?? 0m;
            var effectiveApplicationChargeRate = applicationChargeRate ?? 0m;
            var effectiveAmortizationMode = string.IsNullOrWhiteSpace(amortizationMode) ? "DIM" : amortizationMode;
            var effectiveChargeAdvanceInterest = chargeAdvanceInterest ?? false;

            var existing = await _context.LoanProducts.FirstOrDefaultAsync(p => p.Code == code, ct);

            if (existing == null)
            {
                // New product — create with LastSyncedAt = MinValue so the
                // sync can pick it up and overwrite sync-owned fields on
                // next run if the code exists in webloan.
                var product = new LoanProduct
                {
                    Code = code!,
                    Description = description!,
                    MinAmount = minAmount!.Value,
                    MaxAmount = maxAmount!.Value,
                    MinTermDays = minTermDays!.Value,
                    MaxTermDays = maxTermDays!.Value,
                    NotarialFee = notarialFee,
                    DocStampFee = docStampFee,
                    InsuranceFee = insuranceFee,
                    AdvanceInterestRate = effectiveAdvanceInterestRate,
                    ApplicationChargeRate = effectiveApplicationChargeRate,
                    AmortizationMode = effectiveAmortizationMode,
                    ChargeAdvanceInterest = effectiveChargeAdvanceInterest,
                    IsRetired = isRetired,
                    LastSyncedAt = DateTime.MinValue,
                    UpdatedDate = now,
                    UpdatedById = operatorId,
                };
                await _context.LoanProducts.AddAsync(product, ct);
                created++;

                await _auditLogService.LogAsync(
                    operatorId, "System", "Import", "LoanProduct", code!, description!,
                    "Loan product imported via batch upload");
            }
            else
            {
                // Existing product — update policy fields only. Sync-owned
                // fields (Description, IsRetired) are also overwritten here
                // because the import is the source of truth for the batch.
                // The next sync run will overwrite them again if the code
                // exists in webloan.
                existing.Description = description!;
                existing.MinAmount = minAmount!.Value;
                existing.MaxAmount = maxAmount!.Value;
                existing.MinTermDays = minTermDays!.Value;
                existing.MaxTermDays = maxTermDays!.Value;
                existing.NotarialFee = notarialFee;
                existing.DocStampFee = docStampFee;
                existing.InsuranceFee = insuranceFee;
                existing.AdvanceInterestRate = effectiveAdvanceInterestRate;
                existing.ApplicationChargeRate = effectiveApplicationChargeRate;
                existing.AmortizationMode = effectiveAmortizationMode;
                existing.ChargeAdvanceInterest = effectiveChargeAdvanceInterest;
                existing.IsRetired = isRetired;
                existing.UpdatedDate = now;
                existing.UpdatedById = operatorId;
                updated++;

                await _auditLogService.LogAsync(
                    operatorId, "System", "Import", "LoanProduct", code!, description!,
                    "Loan product updated via batch import");
            }
        }

        await _context.SaveChangesAsync(ct);

        return new LoanProductImportResult(totalRows, created, updated, errors.Count, errors);
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
