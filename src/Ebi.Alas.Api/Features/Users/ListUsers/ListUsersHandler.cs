using Ebi.Alas.Api.Composition.Pagination;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Users.ListUsers;

public sealed class ListUsersHandler(AlasDbContext db)
{
    public async Task<PageResult<UserResponse>> HandleAsync(
        PageRequest page,
        int maxPageSize,
        int defaultPageSize,
        CancellationToken cancellationToken)
    {
        var normalized = page.Normalize(maxPageSize, defaultPageSize);
        var query = db.Users.AsNoTracking().OrderBy(u => u.UserName);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip(normalized.Skip)
            .Take(normalized.PageSize)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.FirstName,
                u.LastName,
                u.MiddleName,
                u.Email,
                u.BranchId,
                u.JobTitle,
                Role = u.Role.ToString(),
                Status = u.Status.ToString(),
                u.MustChangePassword,
                u.CreatedAt
            })
            .ToListAsync(cancellationToken);

        List<UserResponse> items = rows.Count == 0
            ? []
            : [.. rows.Select(r => new UserResponse(
                r.Id,
                r.UserName,
                r.FirstName,
                r.LastName,
                r.MiddleName,
                r.Email,
                r.BranchId,
                r.JobTitle,
                r.Role,
                r.Status,
                r.MustChangePassword,
                r.CreatedAt))];

        return new PageResult<UserResponse>(items, total, normalized.Page, normalized.PageSize);
    }
}