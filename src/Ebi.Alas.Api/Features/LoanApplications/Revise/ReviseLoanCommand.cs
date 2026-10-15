using System.Text.Json;

namespace Ebi.Alas.Api.Features.LoanApplications.Revise;

/// <summary>
/// Immutable fields (LamId, BranchId, ProductCode, ApplicationGroupNo) are omitted on purpose.
/// </summary>
public sealed record ReviseLoanCommand(
    string? ClientName,
    decimal Principal,
    int TermDays,
    decimal InterestRatePerMonth,
    JsonElement? Form,
    string RevisionNotes,
    string? RowVersion);
