using ClosedXML.Excel;

namespace Ebi.Alas.Api.Features.LoanProducts;

/// <summary>xlsx read/write for loan-product import, template, and export.</summary>
public static class LoanProductExcel
{
    /// <summary>
    /// Header labels from the production import template
    /// (Testing Excel/loan-product-import-template.xlsx). Asterisks mark required columns.
    /// </summary>
    public static readonly string[] ImportHeaders =
    [
        "Code *", "Description *", "Min Amount *", "Max Amount *",
        "Min Term Days *", "Max Term Days *", "Notarial Fee",
        "Doc Stamp Fee", "Insurance Fee", "Advance Interest Rate *",
        "Application Charge Rate *", "Amortization Mode *",
        "Charge Advance Interest *", "Is Retired"
    ];

    public sealed record Row(
        int RowNumber,
        string? Code,
        string? Description,
        decimal? MinAmount,
        decimal? MaxAmount,
        int? MinTermDays,
        int? MaxTermDays,
        decimal? NotarialFee,
        decimal? DocStampFee,
        decimal? InsuranceFee,
        decimal? AdvanceInterestRate,
        decimal? ApplicationChargeRate,
        string? AmortizationMode,
        bool? ChargeAdvanceInterest,
        bool? IsRetired);

    public static IReadOnlyList<Row> Parse(Stream xlsx)
    {
        using var workbook = new XLWorkbook(xlsx);
        var sheet = workbook.Worksheets.First();
        var map = MapHeaders(sheet);
        if (!map.ContainsKey("Code"))
        {
            throw new FormatException("Sheet must include a Code column.");
        }

        var rows = new List<Row>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)
        {
            if (IsBlank(sheet, r, map))
            {
                continue;
            }

            rows.Add(new Row(
                r,
                Cell(sheet, r, map, "Code"),
                Cell(sheet, r, map, "Description"),
                Decimal(sheet, r, map, "Min Amount", "MinAmount"),
                Decimal(sheet, r, map, "Max Amount", "MaxAmount"),
                Int(sheet, r, map, "Min Term Days", "MinTermDays"),
                Int(sheet, r, map, "Max Term Days", "MaxTermDays"),
                Decimal(sheet, r, map, "Notarial Fee", "NotarialFee"),
                Decimal(sheet, r, map, "Doc Stamp Fee", "DocStampFee"),
                Decimal(sheet, r, map, "Insurance Fee", "InsuranceFee"),
                Decimal(sheet, r, map, "Advance Interest Rate", "AdvanceInterestRate"),
                Decimal(sheet, r, map, "Application Charge Rate", "ApplicationChargeRate"),
                Cell(sheet, r, map, "Amortization Mode", "AmortizationMode"),
                YesNo(sheet, r, map, "Charge Advance Interest", "ChargeAdvanceInterest"),
                YesNo(sheet, r, map, "Is Retired", "IsRetired")));
        }

        return rows;
    }

    public static byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Template");
        WriteHeaderRow(sheet, ImportHeaders);

        // Production sample rows: rate/mode columns may be blank and default at import.
        object?[][] samples =
        [
            ["A06", "APDS DIM_RSL", 10000, 500000, 180, 2555, 500, 0, 0, null, null, null, null, "No"],
            ["A16", "APDS-RPSU 1-7YR_DIM", 8000, 1200000, 360, 2617, 500, 0, 0, null, null, null, null, "No"],
            ["A17", "APDS-EMP 1-7YR_Dim", 10000, 2000000, 360, 2617, 500, null, null, null, null, null, null, "No"],
            ["C02", "CL - Lumpsum AdvInt", 5000, 100000, 360, 720, 500, null, null, null, null, null, null, "No"],
            ["C35", "CL - BONUS LOAN 2 YEARS", 3000, 200000, 30, 720, 700, null, null, null, null, null, null, "No"],
        ];
        for (var r = 0; r < samples.Length; r++)
        {
            for (var c = 0; c < samples[r].Length; c++)
            {
                if (samples[r][c] is { } value)
                {
                    sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(value);
                }
            }
        }

        var instructions = workbook.Worksheets.Add("Instructions");
        string[] instructionText =
        [
            "Loan Product Import Instructions",
            "Required fields are marked with *",
            "Code: Unique product identifier (e.g., A16, C35). Used as primary key.",
            "Description: Human-readable product name.",
            "Min/Max Amount: Eligibility bounds in PHP. Max must be >= Min.",
            "Min/Max Term Days: Term bounds in days. Max must be >= Min and <= 2,617 (7 years).",
            "Fees: Flat fees in PHP (Notarial, Doc Stamp, Insurance). Cannot be negative.",
            "Advance Interest Rate: Decimal (0.12 = 12% p.a.). Must be 0-1.",
            "Application Charge Rate: Decimal (0.06 = 6%). Must be 0-1.",
            "Amortization Mode: DIM (diminishing) or MIC (minimum installment check).",
            "Charge Advance Interest: Yes/No. True for add-on products.",
            "Is Retired: Yes/No. Defaults to No if omitted.",
            "",
            "Import behavior: Upsert semantics — existing codes are updated, new codes are created.",
            "Sync-owned fields (Description, IsRetired) from imported products will be overwritten on next sync if the code exists in webloan.",
            "Checklist documents are NOT imported — use the product edit form to configure document requirements.",
        ];
        for (var i = 0; i < instructionText.Length; i++)
        {
            instructions.Cell(i + 1, 1).Value = instructionText[i];
        }

        sheet.Columns().AdjustToContents();
        instructions.Columns().AdjustToContents();
        return Save(workbook);
    }

    public static byte[] BuildExport(IReadOnlyList<LoanProductResponse> products)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Loan Products");
        string[] headers =
        [
            "Code", "Description", "Min Amount", "Max Amount",
            "Min Term (Days)", "Max Term (Days)", "Notarial Fee",
            "Doc Stamp Fee", "Insurance Fee", "Advance Interest Rate",
            "Application Charge Rate", "Amortization Mode",
            "Charge Advance Interest", "Is Retired", "Last Synced"
        ];
        WriteHeaderRow(sheet, headers);

        for (var i = 0; i < products.Count; i++)
        {
            var p = products[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = p.Code;
            sheet.Cell(row, 2).Value = p.Description;
            sheet.Cell(row, 3).Value = p.MinAmount;
            sheet.Cell(row, 4).Value = p.MaxAmount;
            sheet.Cell(row, 5).Value = p.MinTermDays;
            sheet.Cell(row, 6).Value = p.MaxTermDays;
            sheet.Cell(row, 7).Value = p.NotarialFee;
            sheet.Cell(row, 8).Value = p.DocStampFee;
            sheet.Cell(row, 9).Value = p.InsuranceFee;
            sheet.Cell(row, 10).Value = p.AdvanceInterestRate;
            sheet.Cell(row, 11).Value = p.ApplicationChargeRate;
            sheet.Cell(row, 12).Value = p.AmortizationMode;
            sheet.Cell(row, 13).Value = p.ChargeAdvanceInterest ? "Yes" : "No";
            sheet.Cell(row, 14).Value = p.IsRetired ? "Yes" : "No";
            sheet.Cell(row, 15).Value = p.LastSyncedAt.ToString("yyyy-MM-dd HH:mm:ss");
        }

        sheet.Columns().AdjustToContents();
        return Save(workbook);
    }

    private static void WriteHeaderRow(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(1, i + 1).Style.Font.Bold = true;
        }
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Dictionary<string, int> MapHeaders(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var c = 1; c <= lastCol; c++)
        {
            var header = sheet.Cell(1, c).GetString().Trim();
            if (header.Length == 0)
            {
                continue;
            }

            var normalized = header.Replace("*", string.Empty).Trim();
            map[normalized] = c;
        }

        return map;
    }

    private static bool IsBlank(IXLWorksheet sheet, int row, Dictionary<string, int> map)
    {
        foreach (var col in map.Values)
        {
            if (!sheet.Cell(row, col).IsEmpty())
            {
                return false;
            }
        }

        return true;
    }

    private static string? Cell(IXLWorksheet sheet, int row, Dictionary<string, int> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (map.TryGetValue(key, out var col))
            {
                var value = sheet.Cell(row, col).GetString().Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    private static decimal? Decimal(IXLWorksheet sheet, int row, Dictionary<string, int> map, params string[] keys)
    {
        var raw = Cell(sheet, row, map, keys);
        return raw is not null && decimal.TryParse(raw, out var value) ? value : null;
    }

    private static int? Int(IXLWorksheet sheet, int row, Dictionary<string, int> map, params string[] keys)
    {
        var raw = Cell(sheet, row, map, keys);
        return raw is not null && int.TryParse(raw, out var value) ? value : null;
    }

    private static bool? YesNo(IXLWorksheet sheet, int row, Dictionary<string, int> map, params string[] keys)
    {
        var raw = Cell(sheet, row, map, keys);
        if (raw is null)
        {
            return null;
        }

        return raw.Equals("Yes", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("True", StringComparison.OrdinalIgnoreCase)
            || raw == "1";
    }
}
