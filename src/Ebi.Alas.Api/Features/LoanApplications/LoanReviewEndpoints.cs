using System.Security.Claims;
using System.Text.Json;
using Ebi.Alas.Api.Composition.Auth;
using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Features.Deviations;
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

public sealed record CreateDeviationRemarkRequest(
    string Body,
    Guid? ParentRemarkId);

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

            var rows = await db.LoanDeviations.AsNoTracking()
                .Where(d => d.LoanApplicationId == loan.Id)
                .OrderBy(d => d.Severity)
                .ThenBy(d => d.Code)
                .ToListAsync(ct);

            var remarkRows = await db.DeviationRemarks.AsNoTracking()
                .Where(r => rows.Select(d => d.Id).Contains(r.LoanDeviationId))
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(ct);

            var authorIds = remarkRows.Select(r => r.AuthorId).Distinct().ToList();
            var authors = await db.Users.AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName, Role = u.Role.ToString() })
                .ToDictionaryAsync(u => u.Id, ct);

            var deviations = rows.Select(d => new LoanDeviationDto(
                d.Id,
                d.Description,
                d.Description,
                false,
                d.Severity,
                remarkRows
                    .Where(r => r.LoanDeviationId == d.Id)
                    .Select(r =>
                    {
                        authors.TryGetValue(r.AuthorId, out var author);
                        return new DeviationRemarkDto(
                            r.Id,
                            r.LoanDeviationId,
                            r.ParentRemarkId,
                            author?.Name ?? "Unknown",
                            author?.Role ?? string.Empty,
                            r.Body,
                            r.CreatedAt);
                    })
                    .ToList())).ToList();

            return Results.Ok(deviations);
        })
        .WithName("GetLoanDeviations");

        group.MapPost("/{id:guid}/deviations/{deviationId:guid}/remarks", async (
            Guid id,
            Guid deviationId,
            CreateDeviationRemarkRequest request,
            AlasDbContext db,
            ClaimsPrincipal user,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var caller = CallerContext.Require(user);
            if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length < 2)
            {
                throw new ValidationException("Remark body is required.");
            }

            if (request.Body.Length > 2000)
            {
                throw new ValidationException("Remark body must not exceed 2000 characters.");
            }

            var loan = await FindAccessibleLoanAsync(db, id, caller, ct)
                ?? throw new NotFoundException("Loan", id.ToString());
            _ = loan;

            var deviation = await db.LoanDeviations.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == deviationId && d.LoanApplicationId == id, ct)
                ?? throw new NotFoundException("Deviation", deviationId.ToString());

            if (request.ParentRemarkId is { } parentId)
            {
                var parentExists = await db.DeviationRemarks.AsNoTracking()
                    .AnyAsync(r => r.Id == parentId && r.LoanDeviationId == deviationId, ct);
                if (!parentExists)
                {
                    throw new ValidationException("Parent remark was not found.");
                }
            }

            var remark = DeviationRemark.Create(
                deviationId,
                request.ParentRemarkId,
                request.Body,
                caller.UserId,
                timeProvider.GetUtcNow());
            db.DeviationRemarks.Add(remark);
            await db.SaveChangesAsync(ct);

            var author = await db.Users.AsNoTracking()
                .Where(u => u.Id == caller.UserId)
                .Select(u => new { Name = u.FirstName + " " + u.LastName, Role = u.Role.ToString() })
                .FirstOrDefaultAsync(ct);

            return Results.Ok(new DeviationRemarkDto(
                remark.Id,
                remark.LoanDeviationId,
                remark.ParentRemarkId,
                author?.Name ?? "Unknown",
                author?.Role ?? string.Empty,
                remark.Body,
                remark.CreatedAt));
        })
        .RequireRateLimiting("write")
        .WithName("CreateDeviationRemark");

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
        var actorNames = await db.Users.AsNoTracking()
            .Where(u => u.Id == loan.CreatedBy
                || (loan.LastActionById != null && u.Id == loan.LastActionById))
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        actorNames.TryGetValue(loan.CreatedBy, out var creatorName);

        var events = new List<TimelineEventDto>
        {
            new(
                $"workflow:created:{loan.Id}",
                "workflow",
                loan.CreatedAt,
                creatorName,
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
            if (actorNames.TryGetValue(action.ActionByUserId, out var cachedName))
            {
                actorName = cachedName;
            }
            else
            {
                actorName = await db.Users.AsNoTracking()
                    .Where(u => u.Id == action.ActionByUserId)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefaultAsync(ct);
            }

            events.Add(new TimelineEventDto(
                $"workflow:action:{action.Id}",
                "workflow",
                action.ActionDate,
                actorName,
                null,
                action.Action,
                action.FromStatus,
                action.ToStatus ?? loan.Status.ToString(),
                action.Comments,
                null,
                null));
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

        AddFormRemarkEvents(events, loan, creatorName);

        var documentRemarks = await db.DocumentRemarks.AsNoTracking()
            .Where(r => r.LoanApplicationId == loan.Id)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.ChecklistIdCode,
                r.Body,
                r.CreatedAt,
                r.AuthorId,
            })
            .ToListAsync(ct);

        foreach (var docRemark in documentRemarks)
        {
            string? authorName = null;
            string? authorRole = null;
            if (actorNames.TryGetValue(docRemark.AuthorId, out var cachedAuthor))
            {
                authorName = cachedAuthor;
            }
            else
            {
                var author = await db.Users.AsNoTracking()
                    .Where(u => u.Id == docRemark.AuthorId)
                    .Select(u => new { Name = u.FirstName + " " + u.LastName, Role = u.Role.ToString() })
                    .FirstOrDefaultAsync(ct);
                authorName = author?.Name;
                authorRole = author?.Role;
            }

            events.Add(new TimelineEventDto(
                $"documentRemark:{docRemark.Id}",
                "documentRemark",
                docRemark.CreatedAt,
                authorName,
                authorRole,
                "DocumentRemarkAdded",
                null,
                null,
                docRemark.Body,
                docRemark.ChecklistIdCode,
                docRemark.ChecklistIdCode));
        }

        var revisions = await db.RevisionRequests.AsNoTracking()
            .Where(r => r.LoanApplicationId == loan.Id)
            .OrderBy(r => r.RequestedAt)
            .Select(r => new { r.Id, r.Section, r.Comment, r.RequestedAt, r.RequestedBy })
            .ToListAsync(ct);

        foreach (var revision in revisions)
        {
            string? requesterName = null;
            if (actorNames.TryGetValue(revision.RequestedBy, out var cachedRequester))
            {
                requesterName = cachedRequester;
            }
            else
            {
                requesterName = await db.Users.AsNoTracking()
                    .Where(u => u.Id == revision.RequestedBy)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefaultAsync(ct);
            }

            events.Add(new TimelineEventDto(
                $"workflow:revision:{revision.Id}",
                "workflow",
                revision.RequestedAt,
                requesterName,
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

    private static void AddFormRemarkEvents(List<TimelineEventDto> events, LoanApplication loan, string? actorName)
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
                AddRemarkEvent(events, loan, actorName, "Verification findings", verification);

                if (!TryGetObject(loanEl, "deviations", out var deviations))
                {
                    continue;
                }

                AddRemarkEvent(events, loan, actorName, "Encoder remarks", GetString(deviations, "remarks"));
                AddRemarkEvent(events, loan, actorName, "Account officer recommendation", GetString(deviations, "aoRecommendation"));
                AddRemarkEvent(events, loan, actorName, "Other remarks", GetString(deviations, "otherRemarks"));
                AddRemarkEvent(events, loan, actorName, "Fee deviation", GetString(deviations, "feeDeviationJustification"));

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
                            actorName,
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
        string? actorName,
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
            actorName,
            null,
            null,
            null,
            null,
            body.Trim(),
            label,
            null));
    }
}
