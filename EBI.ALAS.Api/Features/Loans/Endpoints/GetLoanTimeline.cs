using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Loans.DTOs;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

/// <summary>
/// GET /api/loans/{id}/timeline — the loan's unified history, newest first,
/// server-paged. Reviewers open a file to see its CURRENT state; older context
/// is pulled on demand ("Load earlier events"), which keeps both payload and
/// render bounded for long-lived files.
/// </summary>
public static class GetLoanTimeline
{
    public static void MapGetLoanTimelineEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/loans")
            .WithTags("Loans")
            .RequireAuthorization();

        group.MapGet("/{id:int}/timeline", async (int id, int? page, int? pageSize, AppDbContext db, CancellationToken ct) =>
        {
            var p = Math.Max(page ?? 1, 1);
            var ps = Math.Clamp(pageSize ?? 15, 1, 50);

            var loan = await db.LoanApplications.AsNoTracking()
                .Where(l => l.Id == id)
                .Select(l => new
                {
                    l.ApplicationDate,
                    l.Remarks,
                    l.AoRecommendation,
                    l.OtherRemarks,
                    Creator = l.CreatedBy.FirstName + " " + l.CreatedBy.LastName,
                    CreatorRole = l.CreatedBy.Role,
                })
                .FirstOrDefaultAsync(ct);

            if (loan is null)
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            var actions = await db.LoanActions.AsNoTracking()
                .Where(a => a.LoanApplicationId == id)
                .Select(a => new
                {
                    a.Id, a.Action, a.FromStatus, a.ToStatus, a.Comments, a.ActionDate,
                    Actor = a.ActionByUser.FirstName + " " + a.ActionByUser.LastName,
                    a.ActionByUser.Role,
                })
                .ToListAsync(ct);

            var deviations = await db.LoanDeviations.AsNoTracking()
                .Where(d => d.LoanApplicationId == id)
                .OrderBy(d => d.SortOrder)
                .Select(d => new
                {
                    d.Id, d.ReasonText, d.EncoderJustification, d.IsFeeOverride,
                    Remarks = d.Remarks
                        .OrderBy(r => r.CreatedAt)
                        .Select(r => new
                        {
                            r.Id, r.Body, r.CreatedAt,
                            Author = r.Author.FirstName + " " + r.Author.LastName,
                            r.AuthorRole,
                        })
                        .ToList(),
                })
                .ToListAsync(ct);

            var docRemarks = await db.DocumentRemarks.AsNoTracking()
                .Where(r => r.LoanApplicationId == id)
                .Select(r => new
                {
                    r.Id, r.ChecklistIdCode, r.Body, r.CreatedAt,
                    Author = r.Author.FirstName + " " + r.Author.LastName,
                    r.AuthorRole,
                })
                .ToListAsync(ct);

            // Code → display name so document remarks read like sentences, not keys.
            var checklistNames = await db.DocumentChecklists.AsNoTracking()
                .Where(c => c.LoanApplicationId == id)
                .GroupBy(c => c.Code)
                .Select(g => new { Code = g.Key, Name = g.First().Name })
                .ToDictionaryAsync(x => x.Code, x => x.Name, ct);

            var events = new List<TimelineEventDto>();

            foreach (var a in actions)
            {
                events.Add(new TimelineEventDto
                {
                    Id = $"workflow:{a.Id}",
                    Type = "workflow",
                    OccurredAtUtc = a.ActionDate,
                    ActorName = a.Actor,
                    ActorRole = a.Role,
                    Action = a.Action,
                    FromStatus = a.FromStatus,
                    ToStatus = a.ToStatus,
                    Comment = a.Comments,
                });
            }

            // Submission-time entries: what the encoder declared/remarked up front.
            foreach (var d in deviations)
            {
                events.Add(new TimelineEventDto
                {
                    Id = $"deviation:{d.Id}",
                    Type = "deviation",
                    OccurredAtUtc = loan.ApplicationDate,
                    ActorName = loan.Creator,
                    ActorRole = loan.CreatorRole,
                    Subject = d.IsFeeOverride ? "Fee override" : d.ReasonText,
                    Comment = d.EncoderJustification,
                });

                foreach (var r in d.Remarks)
                {
                    events.Add(new TimelineEventDto
                    {
                        Id = $"deviationRemark:{r.Id}",
                        Type = "deviationRemark",
                        OccurredAtUtc = r.CreatedAt,
                        ActorName = r.Author,
                        ActorRole = r.AuthorRole,
                        Subject = d.IsFeeOverride ? "Fee override" : d.ReasonText,
                        Comment = r.Body,
                    });
                }
            }

            foreach (var r in docRemarks)
            {
                events.Add(new TimelineEventDto
                {
                    Id = $"documentRemark:{r.Id}",
                    Type = "documentRemark",
                    OccurredAtUtc = r.CreatedAt,
                    ActorName = r.Author,
                    ActorRole = r.AuthorRole,
                    Subject = checklistNames.TryGetValue(r.ChecklistIdCode, out var name)
                        ? name
                        : r.ChecklistIdCode,
                    SubjectCode = r.ChecklistIdCode,
                    Comment = r.Body,
                });
            }

            // Submission-time remarks fields on the loan itself.
            AddSubmissionRemark(events, loan.ApplicationDate, loan.Creator, loan.CreatorRole,
                loan.Remarks, "Encoder remarks");
            AddSubmissionRemark(events, loan.ApplicationDate, loan.Creator, loan.CreatorRole,
                loan.AoRecommendation, "Account officer recommendation");
            AddSubmissionRemark(events, loan.ApplicationDate, loan.Creator, loan.CreatorRole,
                loan.OtherRemarks, "Other remarks");

            // Feed order: newest first. Reviewers open a file to see its CURRENT
            // state; older context is pulled on demand ("Load earlier events"),
            // which keeps both payload and render bounded for long-lived files.
            var ordered = events
                .OrderByDescending(e => e.OccurredAtUtc)
                .ThenByDescending(e => e.Id, StringComparer.Ordinal)
                .ToList();

            var totalCount = ordered.Count;
            var pageItems = ordered.Skip((p - 1) * ps).Take(ps).ToList();

            return Results.Ok(ApiResponse<PagedResult<TimelineEventDto>>.SuccessResponse(
                new PagedResult<TimelineEventDto>(pageItems, totalCount, p, ps)));
        })
        .WithName("GetLoanTimeline")
        .Produces<ApiResponse<PagedResult<TimelineEventDto>>>(200)
        .Produces<ApiResponse>(404)
        .RequireAuthorization("CanViewLoan");
    }

    private static void AddSubmissionRemark(
        List<TimelineEventDto> events,
        DateTime applicationDate,
        string actorName,
        string actorRole,
        string? body,
        string label)
    {
        if (string.IsNullOrWhiteSpace(body)) return;

        events.Add(new TimelineEventDto
        {
            Id = $"remark:{label}",
            Type = "remark",
            OccurredAtUtc = applicationDate,
            ActorName = actorName,
            ActorRole = actorRole,
            Subject = label,
            Comment = body,
        });
    }
}
