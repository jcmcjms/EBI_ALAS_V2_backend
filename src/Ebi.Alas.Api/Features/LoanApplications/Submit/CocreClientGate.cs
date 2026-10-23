using System.Text.Json;
using Ebi.Alas.Api.Features.WebLoans;

namespace Ebi.Alas.Api.Features.LoanApplications.Submit;

/// <summary>
/// Blocks submit when WebLoan COCRE checklist (CCR01–CCR11) or required client
/// identity fields are incomplete. Missing items are returned like product docs
/// so the API can list them for the encoder.
/// </summary>
public static class CocreClientGate
{
    public static readonly IReadOnlyList<string> RequiredCocreeItems =
    [
        "CCR01", "CCR02", "CCR03", "CCR04", "CCR05",
        "CCR06", "CCR07", "CCR08", "CCR09", "CCR10", "CCR11",
    ];

    public static async Task<IReadOnlyList<string>> FindMissingAsync(
        IWebLoanReader webLoan,
        JsonElement? form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webLoan);

        var (cisId, formAddress, formContact) = ReadClient(form);
        if (string.IsNullOrWhiteSpace(cisId))
        {
            return ["Client ID is missing"];
        }

        var missing = new List<string>();

        var missingCocree = await webLoan.GetMissingCocreeItemsAsync(
            cisId,
            RequiredCocreeItems,
            cancellationToken);
        foreach (var code in missingCocree)
        {
            missing.Add($"COCRE {code} is missing for client {cisId}");
        }

        var detail = await webLoan.GetCisDetailAsync(cisId, cancellationToken);
        if (detail is null)
        {
            missing.Add($"Client ID {cisId} was not found in WebLoan");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(formAddress)
                && string.IsNullOrWhiteSpace(detail.Borrower.Address))
            {
                missing.Add($"Client {cisId}: address is missing");
            }

            if (string.IsNullOrWhiteSpace(formContact)
                && string.IsNullOrWhiteSpace(detail.Borrower.Phone))
            {
                missing.Add($"Client {cisId}: contact number is missing");
            }
        }

        return missing;
    }

    internal static (string? CisId, string? Address, string? Contact) ReadClient(JsonElement? form)
    {
        if (form is not { } root
            || root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("client", out var client)
            || client.ValueKind != JsonValueKind.Object)
        {
            return (null, null, null);
        }

        return (
            ReadString(client, "cisId"),
            ReadString(client, "address"),
            ReadString(client, "contact") ?? ReadString(client, "contactNumber") ?? ReadString(client, "mobile"));
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
