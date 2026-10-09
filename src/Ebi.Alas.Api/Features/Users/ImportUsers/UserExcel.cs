using ClosedXML.Excel;

namespace Ebi.Alas.Api.Features.Users.ImportUsers;

/// <summary>xlsx read/write for bulk user import (header-based columns).</summary>
public static class UserExcel
{
    /// <summary>Preferred 7-column export/import layout.</summary>
    public static readonly string[] Headers =
    [
        "Username", "First Name", "Middle Name", "Last Name", "Email", "Branch Code", "Role"
    ];

    public sealed record Row(
        int RowNumber,
        string? Username,
        string? FirstName,
        string? MiddleName,
        string? LastName,
        string? Email,
        string? BranchCode,
        string? Role,
        string? JobTitle,
        string? Password,
        bool MustChangePassword);

    public static IReadOnlyList<Row> Parse(Stream xlsx)
    {
        using var workbook = new XLWorkbook(xlsx);
        var sheet = workbook.Worksheets.First();
        var map = MapHeaders(sheet);
        if (!map.ContainsKey("Username") || !map.ContainsKey("Role"))
        {
            throw new FormatException("Sheet must include Username and Role columns.");
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
                Cell(sheet, r, map, "Username"),
                Cell(sheet, r, map, "First Name", "FirstName"),
                Cell(sheet, r, map, "Middle Name", "MiddleName"),
                Cell(sheet, r, map, "Last Name", "LastName"),
                Cell(sheet, r, map, "Email"),
                Cell(sheet, r, map, "Branch Code", "BranchCode", "Branch"),
                Cell(sheet, r, map, "Role"),
                Cell(sheet, r, map, "Job Title", "JobTitle"),
                Cell(sheet, r, map, "Password"),
                ParseMustChange(Cell(sheet, r, map, "MustChangePassword", "MustChange"))));
        }

        return rows;
    }

    public static byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Template");
        string[] headers =
        [
            "Username *", "First Name *", "Middle Name", "Last Name *",
            "Branch Code *", "Role *", "Job Title", "Covered Branches",
            "Email", "Phone", "MustChangePassword", "Password"
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        sheet.Cell(2, 1).Value = "jdoe";
        sheet.Cell(2, 2).Value = "Juan";
        sheet.Cell(2, 3).Value = "Dela";
        sheet.Cell(2, 4).Value = "Cruz";
        sheet.Cell(2, 5).Value = "011";
        sheet.Cell(2, 6).Value = "Encoder";
        sheet.Cell(2, 7).Value = "Account Officer";
        sheet.Cell(2, 9).Value = "jdoe@enterprisebank.ph";

        var notes = workbook.Worksheets.Add("Instructions");
        notes.Cell(1, 1).Value = "Role must be one of:";
        notes.Cell(2, 1).Value = "Encoder";
        notes.Cell(3, 1).Value = "Recommender";
        notes.Cell(4, 1).Value = "Evaluator";
        notes.Cell(5, 1).Value = "Approver";
        notes.Cell(6, 1).Value = "Admin";
        notes.Cell(8, 1).Value = "Job Title is a bank title / authority key (e.g. Account Officer, BranchHead).";
        notes.Cell(9, 1).Value = "Branch Code is a code such as 011 (not a branch name).";
        notes.Cell(10, 1).Value = "Leave Password blank to auto-generate. MustChangePassword: 0 = false, blank/1 = true.";
        notes.Cell(11, 1).Value = "Empty rows are ignored. Row numbers in errors match Excel (header = row 1).";

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static Dictionary<string, int> MapHeaders(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        for (var c = 1; c <= lastCol; c++)
        {
            var raw = sheet.Cell(1, c).GetString().Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            // "Username *" → Username
            var star = raw.IndexOf('*');
            if (star >= 0)
            {
                raw = raw[..star].Trim();
            }

            var key = NormalizeHeader(raw);
            if (key.Length > 0)
            {
                map.TryAdd(key, c);
            }
        }

        return map;
    }

    private static string NormalizeHeader(string raw) => raw
        .Replace("_", " ", StringComparison.Ordinal)
        .Replace("  ", " ", StringComparison.Ordinal)
        .Trim();

    private static bool IsBlank(IXLWorksheet sheet, int row, Dictionary<string, int> map)
    {
        foreach (var col in map.Values)
        {
            if (!string.IsNullOrWhiteSpace(sheet.Cell(row, col).GetString()))
            {
                return false;
            }
        }

        return true;
    }

    private static string? Cell(IXLWorksheet sheet, int row, Dictionary<string, int> map, params string[] names)
    {
        foreach (var name in names)
        {
            if (!map.TryGetValue(NormalizeHeader(name), out var col))
            {
                continue;
            }

            var value = sheet.Cell(row, col).GetString().Trim().Trim('﻿', ' ');
            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    private static bool ParseMustChange(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return !(raw == "0" || raw.Equals("false", StringComparison.OrdinalIgnoreCase));
    }
}
