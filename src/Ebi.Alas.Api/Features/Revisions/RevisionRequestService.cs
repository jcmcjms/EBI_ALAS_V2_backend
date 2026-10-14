using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Revisions;

public sealed record RevisionSectionFeedbackDto(
    string SectionId,
    string SectionLabel,
    string Comments);

public sealed record RevisionRequestDto(
    Guid Id,
    Guid LoanApplicationId,
    string RequestedByName,
    string RequestedByRole,
    string OverallComments,
    string FromStatus,
    DateTimeOffset RequestedAt,
    bool IsResolved,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<RevisionSectionFeedbackDto> Sections);

public sealed class RevisionRequestService(AlasDbContext db, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<RevisionRequestDto>> ListAsync(
        Guid loanApplicationId,
        CancellationToken cancellationToken)
    {
        var rows = await db.RevisionRequests.AsNoTracking()
            .Where(r => r.LoanApplicationId == loanApplicationId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var userIds = rows.Select(r => r.RequestedBy).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName, Role = u.Role })
            .ToListAsync(cancellationToken);
        var byUser = users.ToDictionary(u => u.Id);

        return rows.Select(r =>
        {
            byUser.TryGetValue(r.RequestedBy, out var author);
            var sectionIds = r.Section.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new RevisionRequestDto(
                r.Id,
                r.LoanApplicationId,
                author?.Name ?? "Unknown",
                author?.Role.ToString() ?? string.Empty,
                r.Comment,
                FromStatus: string.Empty,
                r.RequestedAt,
                r.IsResolved,
                r.ResolvedAt,
                sectionIds.Select(id => new RevisionSectionFeedbackDto(
                    id,
                    id,
                    Comments: string.Empty)).ToList());
        }).ToList();
    }

    public async Task<bool> ResolveAsync(
        Guid loanApplicationId,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        var request = await db.RevisionRequests
            .FirstOrDefaultAsync(
                r => r.Id == revisionId && r.LoanApplicationId == loanApplicationId,
                cancellationToken);
        if (request is null || request.IsResolved)
        {
            return false;
        }

        request.Resolve(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
