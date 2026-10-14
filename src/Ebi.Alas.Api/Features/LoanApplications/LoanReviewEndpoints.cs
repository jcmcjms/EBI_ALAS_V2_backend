using System.Security.Claims;
using System.Text.Json;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications;

public sealed record TimelineEventDto(
    string Id,
    string Type,
    DateTimeOffset OccurredAtUtc,
    string? ActorName,
    string? ActorRole,
    string? Action,
    string? FromStatus,
    string? ToStatus,
    string? Comment,
    string? Subject,
    string? SubjectCode);

public sealed record DeviationRemarkDto(
    Guid Id,
    Guid LoanDeviationId,
    Guid? ParentRemarkId,
    string AuthorName,
    string AuthorRole,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record LoanDeviationDto(
    Guid Id,
    string ReasonText,
    string EncoderJustification,
    bool IsFeeOverride,
    int SortOrder,
    IReadOnlyList<DeviationRemarkDto> Remarks);

public sealed record RoutingEvaluatedInputsDto(
    string Cycle,
    string Severity,
    decimal Exposure);

public sealed record LoanRoutingResponseDto(
    Guid LoanId,
    int? RequiredApprovalTier,
    int DeviationSeverity,
    decimal TotalExposure,
    string LoanType,
    string? MatchedRule,
    bool DocumentsComplete,
    IReadOnlyList<string> MissingDocuments,
    Guid? AssignedApproverId,
    string? AssignedApproverName,
    int? EscalatedFromTier,
    string? NoAuthorityReason,
    RoutingEvaluatedInputsDto? Evaluated);

public static class LoanReviewEndpoints
{
    public static void MapLoanReview(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans").WithTags("Loans").RequireAuthorization();

        group.MapGet("/{id:guid}/history", async (
            Guid id,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var loan = await FindAccessibleLoanAsync(db, id, caller, ct)
                ?? throw new NotFoundException("Loan", id.ToString());

            var rows = await db.LoanApplicationActions.AsNoTracking()
                .Where(a => a.LoanApplicationId == loan.Id)
                .OrderBy(a => a.ActionDate)
                .Select(a => new
                {
                    a.Id,
                    a.Action,
                    a.ActionDate,
                    a.ActionByUserId,
                    a.FromStatus,
                    a.ToStatus,
                    a.Comments,
                })
                .ToListAsync(ct);

            var userIds = rows.Select(r => r.ActionByUserId).Distinct().ToList();
            var names = await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);

            var result = rows.Select(r =>
            {
                names.TryGetValue(r.ActionByUserId, out var name);
                return new
                {
                    id = r.Id,
                    actionBy = name ?? string.Empty,
                    action = r.Action,
                    fromStatus = r.FromStatus,
                    toStatus = r.ToStatus,
                    comments = r.Comments,
                    actionDate = r.ActionDate,
                };
            }).ToList();

            return Results.Ok(result);
        })
        .WithName("GetLoanHistory");

        group.MapGet("/{id:guid}/timeline", async (
            Guid id,
            int? page,
            int? pageSize,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var loan = await FindAccessibleLoanAsync(db, id, caller, ct)
                ?? throw new NotFoundException("Loan", id.ToString());

            var p = Math.Max(page ?? 1, 1);
            var ps = Math.Clamp(pageSize ?? 15, 1, 50);

            var events = await BuildTimelineEventsAsync(db, loan, ct);
            var ordered = events
                .OrderByDescending(e => e.OccurredAtUtc)
                .ThenByDescending(e => e.Id, StringComparer.Ordinal)
                .ToList();

            var totalCount = ordered.Count;
            var items = ordered
                .Skip((p - 1) * ps)
                .Take(ps)
                .ToList();

            return Results.Ok(new PageResult<TimelineEventDto>(items, totalCount, p, ps));
        })
        .WithName("GetLoanTimeline");

        group.MapGet("/{id:guid}/deviations", async (
            Guid id,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var loan = await FindAccessibleLoanAsync(db, id, caller, ct)
                ?? throw new NotFoundException("Loan", id.ToString());

            var deviations = await db.LoanDeviations.AsNoTracking()
                .Where(d => d.LoanApplicationId == loan.Id)
                .OrderBy(d => d.Severity)
                .ThenBy(d => d.Code)
                .Select(d => new LoanDeviationDto(
                    d.Id,
                    d.Description,
                    d.Description,
                    false,
                    0,
                    Array.Empty<DeviationRemarkDto>()))
                .ToListAsync(ct);

            return Results.Ok(deviations);
        })
        .WithName("GetLoanDeviations");

        group.MapGet("/{id:guid}/routing", async (
            Guid id,
            AlasDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            var loan = await FindAccessibleLoanAsync(db, id, caller, ct)
                ?? throw new NotFoundException("Loan", id.ToString());

            var missingDocuments = await db.DocumentChecklistItems.AsNoTracking()
                .Where(d => d.LoanApplicationId == loan.Id && !d.IsSubmitted)
                .Select(d => d.Name)
                .OrderBy(n => n)
                .ToListAsync(ct);

            var maxSeverity = await db.LoanDeviations.AsNoTracking()
                .Where(d => d.LoanApplicationId == loan.Id)
                .Select(d => (int?)d.Severity)
                .MaxAsync(ct) ?? 0;

            string? assignedApproverName = null;
            if (loan.LastActionById is { } actorId)
            {
                assignedApproverName = await db.Users.AsNoTracking()
                    .Where(u => u.Id == actorId)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefaultAsync(ct);
            }

            var response = new LoanRoutingResponseDto(
                loan.Id,
                RequiredApprovalTier: null,
                DeviationSeverity: maxSeverity,
                TotalExposure: loan.Principal + loan.TotalInterest,
                LoanType: loan.LoanType.ToString(),
                MatchedRule: null,
                DocumentsComplete: missingDocuments.Count == 0,
                MissingDocuments: missingDocuments,
                AssignedApproverId: loan.LastActionById,
                AssignedApproverName: assignedApproverName,
                EscalatedFromTier: null,
                NoAuthorityReason: null,
                Evaluated: null);

            return Results.Ok(response);
        })
        .WithName("GetLoanRouting");
    }

    private static async Task<LoanApplication?> FindAccessibleLoanAsync(
        AlasDbContext db,
        Guid id,
        CallerContext caller,
        CancellationToken ct)
    {
        var query = db.LoanApplications.AsNoTracking().Where(l => l.Id == id);
        if (!caller.CanAccessAllBranches)
        {
            query = query.Where(l => l.BranchId == caller.BranchId);
        }

        return await query.FirstOrDefaultAsync(ct);
    }

    public static async Task<List<TimelineEventDto>> BuildTimelineEventsAsync(
        AlasDbContext db,
        LoanApplication loan,
        CancellationToken ct)
    {
        var events = new List<TimelineEventDto>
        {
            new(
                $"workflow:created:{loan.Id}",
                "workflow",
                loan.CreatedAt,
                null,
                null,
                "Created",
                null,
                LoanStatus.ForRecommendation.ToString(),
                null,
                null,
                null),
        };

        var actionRows = await db.LoanApplicationActions.AsNoTracking()
            .Where(a => a.LoanApplicationId == loan.Id)
            .OrderBy(a => a.ActionDate)
            .Select(a => new
            {
                a.Id,
                a.Action,
                a.ActionDate,
                a.ActionByUserId,
                a.FromStatus,
                a.ToStatus,
                a.Comments,
            })
            .ToListAsync(ct);

        foreach (var action in actionRows)
        {
            string? actorName = null;
            actorName = await db.Users.AsNoTracking()
                .Where(u => u.Id == action.ActionByUserId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync(ct);

            events.Add(new TimelineEventDto(
                $"workflow:action:{action.Id}",
                "workflow",
                action.ActionDate,
                actorName,
                null,
                action.Action,
                null,
                action.ToStatus ?? loan.Status.ToString(),
                null,
                null,
                action.Comments));
        }

        var signatures = await db.SignatureChainEntries.AsNoTracking()
            .Where(s => s.LoanApplicationId == loan.Id && s.SignedAt != null)
            .OrderBy(s => s.SignedAt)
            .Select(s => new
            {
                s.Id,
                s.RoleName,
                s.SignedAt,
                s.UserId,
            })
            .ToListAsync(ct);

        foreach (var signature in signatures)
        {
            string? signerName = null;
            if (signature.UserId is { } signerId)
            {
                signerName = await db.Users.AsNoTracking()
                    .Where(u => u.Id == signerId)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefaultAsync(ct);
            }

            events.Add(new TimelineEventDto(
                $"workflow:signature:{signature.Id}",
                "workflow",
                signature.SignedAt!.Value,
                signerName,
                signature.RoleName,
                "Signed",
                null,
                loan.Status.ToString(),
                null,
                signature.RoleName,
                null));
        }

        var deviations = await db.LoanDeviations.AsNoTracking()
            .Where(d => d.LoanApplicationId == loan.Id)
            .OrderBy(d => d.Code)
            .Select(d => new { d.Id, d.Code, d.Description })
            .ToListAsync(ct);

        foreach (var deviation in deviations)
        {
            events.Add(new TimelineEventDto(
                $"deviation:{deviation.Id}",
                "deviation",
                loan.CreatedAt,
                null,
                null,
                null,
                null,
                null,
                deviation.Description,
                deviation.Description,
                deviation.Code));
        }

        AddFormRemarkEvents(events, loan);

        var revisions = await db.RevisionRequests.AsNoTracking()
            .Where(r => r.LoanApplicationId == loan.Id)
            .OrderBy(r => r.RequestedAt)
            .Select(r => new { r.Id, r.Section, r.Comment, r.RequestedAt })
            .ToListAsync(ct);

        foreach (var revision in revisions)
        {
            events.Add(new TimelineEventDto(
                $"workflow:revision:{revision.Id}",
                "workflow",
                revision.RequestedAt,
                null,
                null,
                "PushedBack",
                loan.Status.ToString(),
                LoanStatus.ForRevision.ToString(),
                revision.Comment,
                revision.Section,
                null));
        }

        return events;
    }

    private static void AddFormRemarkEvents(List<TimelineEventDto> events, LoanApplication loan)
    {
        if (string.IsNullOrWhiteSpace(loan.FormJson))
        {
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(loan.FormJson);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            foreach (var loanEl in EnumerateLoans(doc.RootElement))
            {
                var verification = GetString(loanEl, "verification", "findings");
                AddRemarkEvent(events, loan, "Verification findings", verification);

                if (!TryGetObject(loanEl, "deviations", out var deviations))
                {
                    continue;
                }

                AddRemarkEvent(events, loan, "Encoder remarks", GetString(deviations, "remarks"));
                AddRemarkEvent(events, loan, "Account officer recommendation", GetString(deviations, "aoRecommendation"));
                AddRemarkEvent(events, loan, "Other remarks", GetString(deviations, "otherRemarks"));
                AddRemarkEvent(events, loan, "Fee deviation", GetString(deviations, "feeDeviationJustification"));

                if (TryGetObject(deviations, "deviationJustifications", out var justifications))
                {
                    foreach (var prop in justifications.EnumerateObject())
                    {
                        var body = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(body))
                        {
                            continue;
                        }

                        events.Add(new TimelineEventDto(
                            $"deviationRemark:{prop.Name}",
                            "deviationRemark",
                            loan.CreatedAt,
                            null,
                            null,
                            null,
                            null,
                            null,
                            body.Trim(),
                            prop.Name,
                            null));
                    }
                }
            }
        }
    }

    private static IEnumerable<JsonElement> EnumerateLoans(JsonElement root)
    {
        if (root.TryGetProperty("loans", out var loans) && loans.ValueKind == JsonValueKind.Array)
        {
            foreach (var loanEl in loans.EnumerateArray())
            {
                yield return loanEl;
            }
        }
        else
        {
            yield return root;
        }
    }

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var el)
            || el.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        value = el;
        return true;
    }

    private static string? GetString(JsonElement parent, params string[] path)
    {
        var current = parent;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(name, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static void AddRemarkEvent(
        List<TimelineEventDto> events,
        LoanApplication loan,
        string label,
        string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        events.Add(new TimelineEventDto(
            $"remark:{label}:{loan.Id}",
            "remark",
            loan.CreatedAt,
            null,
            null,
            null,
            null,
            null,
            body.Trim(),
            label,
            null));
    }
}
