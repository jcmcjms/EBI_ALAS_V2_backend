using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Features.Loans.DTOs;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans.Endpoints;

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
            var offset = (p - 1) * ps;

            var exists = await db.LoanApplications.AsNoTracking()
                .AnyAsync(l => l.Id == id, ct);
            if (!exists)
                return Results.NotFound(ApiResponse.ErrorResponse("Loan not found"));

            var sql = """
                WITH AllEvents AS (
                    -- LoanActions → workflow events
                    SELECT
                        CONCAT('workflow:', la.Id) AS Id,
                        'workflow' AS [Type],
                        la.ActionDate AS OccurredAtUtc,
                        CONCAT(u.FirstName, ' ', u.LastName) AS ActorName,
                        u.Role AS ActorRole,
                        la.[Action] AS [Action],
                        la.FromStatus,
                        la.ToStatus,
                        la.Comments AS Comment,
                        CAST(NULL AS NVARCHAR(500)) AS Subject,
                        CAST(NULL AS NVARCHAR(50)) AS SubjectCode
                    FROM LoanActions la
                    LEFT JOIN Users u ON u.Id = la.ActionByUserId
                    WHERE la.LoanApplicationId = @loanId

                    UNION ALL

                    -- LoanDeviations → deviation events
                    SELECT
                        CONCAT('deviation:', ld.Id),
                        'deviation',
                        app.ApplicationDate,
                        CONCAT(cu.FirstName, ' ', cu.LastName),
                        cu.Role,
                        CAST(NULL AS NVARCHAR(50)),
                        CAST(NULL AS NVARCHAR(50)),
                        CAST(NULL AS NVARCHAR(50)),
                        ld.EncoderJustification,
                        CASE WHEN ld.IsFeeOverride = 1 THEN N'Fee override' ELSE ld.ReasonText END,
                        CAST(NULL AS NVARCHAR(50))
                    FROM LoanDeviations ld
                    INNER JOIN LoanApplications app ON app.Id = ld.LoanApplicationId
                    INNER JOIN Users cu ON cu.Id = app.CreatedById
                    WHERE ld.LoanApplicationId = @loanId

                    UNION ALL

                    -- DeviationRemarks → deviationRemark events
                    SELECT
                        CONCAT('deviationRemark:', dr.Id),
                        'deviationRemark',
                        dr.CreatedAt,
                        CONCAT(au.FirstName, ' ', au.LastName),
                        dr.AuthorRole,
                        CAST(NULL AS NVARCHAR(50)),
                        CAST(NULL AS NVARCHAR(50)),
                        CAST(NULL AS NVARCHAR(50)),
                        dr.Body,
                        CASE WHEN ld.IsFeeOverride = 1 THEN N'Fee override' ELSE ld.ReasonText END,
                        CAST(NULL AS NVARCHAR(50))
                    FROM DeviationRemarks dr
                    INNER JOIN LoanDeviations ld ON ld.Id = dr.LoanDeviationId
                    LEFT JOIN Users au ON au.Id = dr.AuthorId
                    WHERE ld.LoanApplicationId = @loanId

                    UNION ALL

                    -- DocumentRemarks → documentRemark events
                    SELECT
                        CONCAT('documentRemark:', dr.Id),
                        'documentRemark',
                        dr.CreatedAt,
                        CONCAT(au.FirstName, ' ', au.LastName),
                        dr.AuthorRole,
                        CAST(NULL AS NVARCHAR(50)),
                        CAST(NULL AS NVARCHAR(50)),
                        CAST(NULL AS NVARCHAR(50)),
                        dr.Body,
                        COALESCE(dc.Name, dr.ChecklistIdCode),
                        dr.ChecklistIdCode
                    FROM DocumentRemarks dr
                    LEFT JOIN Users au ON au.Id = dr.AuthorId
                    LEFT JOIN (
                        SELECT Code, MIN(Name) AS Name
                        FROM DocumentChecklists
                        WHERE LoanApplicationId = @loanId
                        GROUP BY Code
                    ) dc ON dc.Code = dr.ChecklistIdCode
                    WHERE dr.LoanApplicationId = @loanId

                    UNION ALL

                    -- Submission remarks from LoanApplication
                    SELECT
                        CONCAT('remark:', sr.Label),
                        'remark',
                        app.ApplicationDate,
                        CONCAT(cu.FirstName, ' ', cu.LastName),
                        cu.Role,
                        CAST(NULL AS NVARCHAR(50)),
                        CAST(NULL AS NVARCHAR(50)),
                        CAST(NULL AS NVARCHAR(50)),
                        sr.Body,
                        sr.Label,
                        CAST(NULL AS NVARCHAR(50))
                    FROM LoanApplications app
                    INNER JOIN Users cu ON cu.Id = app.CreatedById
                    CROSS APPLY (
                        VALUES
                            (N'Encoder remarks', app.Remarks),
                            (N'Account officer recommendation', app.AoRecommendation),
                            (N'Other remarks', app.OtherRemarks)
                    ) AS sr(Label, Body)
                    WHERE app.Id = @loanId
                      AND sr.Body IS NOT NULL
                      AND LTRIM(RTRIM(sr.Body)) <> N''
                )
                SELECT
                    Id, [Type], OccurredAtUtc, ActorName, ActorRole,
                    [Action], FromStatus, ToStatus, Comment, Subject, SubjectCode,
                    COUNT(*) OVER() AS TotalCount
                FROM AllEvents
                ORDER BY OccurredAtUtc DESC, Id DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
                """;

            var parameters = new object[]
            {
                new SqlParameter("@loanId", id),
                new SqlParameter("@offset", offset),
                new SqlParameter("@pageSize", ps),
            };

            var pageItems = await db.Database
                .SqlQueryRaw<TimelineEventDto>(sql, parameters)
                .ToListAsync(ct);
            var totalCount = pageItems.Count > 0 ? pageItems[0].TotalCount : 0;

            return Results.Ok(ApiResponse<PagedResult<TimelineEventDto>>.SuccessResponse(
                new PagedResult<TimelineEventDto>(pageItems, totalCount, p, ps)));
        })
        .WithName("GetLoanTimeline")
        .Produces<ApiResponse<PagedResult<TimelineEventDto>>>(200)
        .Produces<ApiResponse>(404)
        .RequireAuthorization("CanViewLoan");
    }
}