using System.Globalization;
using System.Text.Json;
using Ebi.Alas.Api.Composition.Errors;

namespace Ebi.Alas.Api.Features.LoanApplications;

/// <summary>
/// Payslip (NTHP) dates must be recent enough for the application day:
/// 1st–15th → from the 16th two months back; 16th–end → from the 1st of last month.
/// </summary>
public static class NthpDateGuard
{
    public static void EnsureAcceptable(JsonElement? form, DateOnly today)
    {
        if (form is not { } root)
        {
            return;
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("loans", out var loans)
            || loans.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var loan in loans.EnumerateArray())
        {
            EnsureDateAcceptable(ReadNthpDate(loan), today);
        }
    }

    public static DateOnly GetWindowStart(DateOnly today)
    {
        if (today.Day <= 15)
        {
            var twoMonthsBack = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
            return new DateOnly(twoMonthsBack.Year, twoMonthsBack.Month, 16);
        }

        return new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
    }

    internal static void EnsureDateAcceptable(string? nthpDate, DateOnly today)
    {
        var raw = nthpDate?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            throw new ValidationException("Add the payslip date before continuing.");
        }

        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new ValidationException("Enter the payslip date as YYYY-MM-DD.");
        }

        if (date > today)
        {
            throw new ValidationException("The payslip date can't be in the future.");
        }

        var windowStart = GetWindowStart(today);
        if (date < windowStart)
        {
            throw new ValidationException(
                $"Use a payslip from {windowStart.ToString("MMMM d", CultureInfo.InvariantCulture)} or later.");
        }
    }

    private static string? ReadNthpDate(JsonElement loan)
    {
        if (loan.ValueKind != JsonValueKind.Object
            || !loan.TryGetProperty("parameters", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("nthpDate", out var nthp)
            || nthp.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return nthp.GetString();
    }
}
