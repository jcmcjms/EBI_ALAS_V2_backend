using System.Text.Json;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

public static class LoanMapping
{
    public static LoanResponse ToResponse(
        this LoanApplication loan,
        string? lastActionByName = null,
        string? assignedToName = null) => new(
        loan.Id,
        loan.LamId,
        loan.ApplicationGroupNo,
        loan.ClientName,
        loan.BranchId,
        loan.ProductCode,
        loan.Product,
        loan.LoanType.ToString(),
        loan.Status.ToString(),
        loan.Principal,
        loan.TermDays,
        loan.InterestRate,
        loan.TotalInterest,
        loan.TotalDeductions,
        loan.NetProceeds,
        loan.CreatedBy,
        loan.CreatedAt,
        loan.UpdatedAt,
        loan.LastActionDate,
        loan.LastActionById,
        lastActionByName,
        loan.LastActionVerb,
        ParseForm(loan.FormJson),
        loan.DocumentsFlaggedAt is { } flaggedAt
            ? new DocumentFlagDto(
                flaggedAt,
                loan.DocumentsFlaggedById,
                loan.DocumentFlagReason,
                loan.DocumentFlagMissingCount)
            : null,
        loan.AssignedToUserId,
        assignedToName,
        Convert.ToBase64String(loan.RowVersion));

    private static JsonElement? ParseForm(string? formJson)
    {
        if (string.IsNullOrWhiteSpace(formJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(formJson);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<LoanResponse> ToResponseAsync(
        this LoanApplication loan,
        AlasDbContext db,
        CancellationToken cancellationToken)
    {
        string? lastActionByName = null;
        if (loan.LastActionById is { } actorId)
        {
            lastActionByName = await db.Users.AsNoTracking()
                .Where(u => u.Id == actorId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? assignedToName = null;
        if (loan.AssignedToUserId is { } assigneeId)
        {
            assignedToName = await db.Users.AsNoTracking()
                .Where(u => u.Id == assigneeId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            assignedToName = await Approvals.ApproverAssignmentService.GetSharedPoolDisplayAsync(
                db, loan, cancellationToken);
        }

        return loan.ToResponse(lastActionByName, assignedToName);
    }
}
