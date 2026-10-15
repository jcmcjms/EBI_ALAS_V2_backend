using System.Text.Json;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Deviations;

/// <summary>
/// Declared policy deviations live in the form snapshot for display; review
/// still needs LoanDeviations rows so remarks can attach. Sync on submit/revise.
/// </summary>
public static class LoanDeviationSync
{
    public static async Task SyncFromFormAsync(
        AlasDbContext db,
        LoanApplication loan,
        string? formJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(formJson))
        {
            return;
        }

        var reasons = ExtractReasons(formJson);
        var existing = await db.LoanDeviations
            .Where(d => d.LoanApplicationId == loan.Id)
            .ToListAsync(cancellationToken);

        var existingCodes = existing
            .Select(d => d.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var reason in reasons)
        {
            var code = ResolveCode(reason);
            if (existingCodes.Contains(code))
            {
                continue;
            }

            var severity = ResolveSeverity(reason);
            db.LoanDeviations.Add(LoanDeviation.Create(
                loan.Id, code, reason, severity, DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static IReadOnlyList<string> ExtractReasons(string formJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(formJson);
            var reasons = new List<string>();
            CollectReasons(doc.RootElement, reasons);
            return reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void CollectReasons(JsonElement root, List<string> reasons)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("deviationDetails", out var details)
                && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in details.EnumerateArray())
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        reasons.Add(text.Trim());
                    }
                }
            }

            foreach (var prop in root.EnumerateObject())
            {
                CollectReasons(prop.Value, reasons);
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                CollectReasons(item, reasons);
            }
        }
    }

    private static string ResolveCode(string reason)
    {
        var match = DeviationCatalog.All.FirstOrDefault(c =>
            string.Equals(c.Description, reason, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? $"DEV-{Math.Abs(reason.GetHashCode()):X8}"
            : match.Id.ToString();
    }

    private static int ResolveSeverity(string reason)
    {
        var match = DeviationCatalog.All.FirstOrDefault(c =>
            string.Equals(c.Description, reason, StringComparison.OrdinalIgnoreCase));
        return match?.Severity ?? 1;
    }
}
