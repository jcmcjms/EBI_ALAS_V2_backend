using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.Users.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Approvals;

public static class ApprovalAuthorityEndpoints
{
    public static void MapApprovalAuthorities(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/approval-authorities").WithTags("Approvals");

        group.MapGet("/", ListAsync)
            .RequireAuthorization()
            .WithName("GetApprovalAuthorities")
            .Produces<IReadOnlyList<ApprovalAuthorityResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization("CanManageWorkflow")
            .RequireRateLimiting("write")
            .WithName("CreateApprovalAuthority")
            .Produces<ApprovalAuthorityResponse>(StatusCodes.Status201Created);

        group.MapPut("/{key}", UpdateAsync)
            .RequireAuthorization("CanManageWorkflow")
            .RequireRateLimiting("write")
            .WithName("UpdateApprovalAuthority")
            .Produces<ApprovalAuthorityResponse>(StatusCodes.Status200OK);

        group.MapDelete("/{key}", DeleteAsync)
            .RequireAuthorization("CanManageWorkflow")
            .RequireRateLimiting("write")
            .WithName("DeleteApprovalAuthority")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPut("/{key}/users", SetUsersAsync)
            .RequireAuthorization("CanManageWorkflow")
            .RequireRateLimiting("write")
            .WithName("SetApprovalAuthorityUsers")
            .Produces<ApprovalAuthorityResponse>(StatusCodes.Status200OK);
    }

    private static async Task<IReadOnlyList<ApprovalAuthorityResponse>> ListAsync(
        AlasDbContext db,
        CancellationToken ct)
    {
        var authorities = await db.ApprovalAuthorities.AsNoTracking()
            .OrderBy(a => a.Tier)
            .ThenBy(a => a.Priority)
            .ToListAsync(ct);

        var assignments = await db.ApprovalAuthorityUsers.AsNoTracking().ToListAsync(ct);
        var names = await LoadNamesAsync(db, assignments.Select(x => x.UserId).Distinct().ToList(), ct);
        var byAuthority = assignments
            .GroupBy(x => x.ApprovalAuthorityId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AssignedApproverResponse>)g
                    .Select(x => new AssignedApproverResponse(x.UserId.ToString(), names.GetValueOrDefault(x.UserId, "Unknown")))
                    .ToList());

        return authorities
            .Select(a => ToResponse(a, byAuthority.GetValueOrDefault(a.Id, [])))
            .ToList();
    }

    private static async Task<IResult> CreateAsync(
        CreateApprovalAuthorityRequest request,
        AlasDbContext db,
        CancellationToken ct)
    {
        ApprovalAuthority authority;
        try
        {
            authority = ApprovalAuthority.Create(
                request.DisplayName,
                request.Tier,
                request.Priority,
                request.AllowNew,
                request.AllowRenewal,
                request.MaxSeverity,
                request.MaxTotalExposure,
                request.ScopeType);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ex.Message);
        }

        if (await db.ApprovalAuthorities.AnyAsync(a => a.Key == authority.Key, ct))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: $"Approval authority '{authority.Key}' already exists.");
        }

        db.ApprovalAuthorities.Add(authority);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/approval-authorities/{authority.Key}", ToResponse(authority, []));
    }

    private static async Task<IResult> UpdateAsync(
        string key,
        UpdateApprovalAuthorityRequest request,
        AlasDbContext db,
        CancellationToken ct)
    {
        var authority = await db.ApprovalAuthorities.FirstOrDefaultAsync(a => a.Key == key, ct)
            ?? throw new NotFoundException("Approval authority", key);

        try
        {
            authority.Update(
                request.DisplayName,
                request.Tier,
                request.Priority,
                request.AllowNew,
                request.AllowRenewal,
                request.MaxSeverity,
                request.MaxTotalExposure,
                request.ScopeType);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(authority, await GetAssignedUsersAsync(db, authority.Id, ct)));
    }

    private static async Task<IResult> DeleteAsync(
        string key,
        AlasDbContext db,
        CancellationToken ct)
    {
        var authority = await db.ApprovalAuthorities.FirstOrDefaultAsync(a => a.Key == key, ct)
            ?? throw new NotFoundException("Approval authority", key);

        db.ApprovalAuthorities.Remove(authority);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetUsersAsync(
        string key,
        SetApprovalAuthorityUsersRequest request,
        AlasDbContext db,
        CancellationToken ct)
    {
        var authority = await db.ApprovalAuthorities.FirstOrDefaultAsync(a => a.Key == key, ct)
            ?? throw new NotFoundException("Approval authority", key);

        var userIds = ParseUserIds(request.UserIds);
        await EnsureApproversAsync(db, userIds, ct);

        var existing = await db.ApprovalAuthorityUsers
            .Where(x => x.ApprovalAuthorityId == authority.Id)
            .ToListAsync(ct);

        var existingIds = existing.Select(x => x.UserId).ToHashSet();
        var toRemove = existing.Where(x => !userIds.Contains(x.UserId)).ToList();
        var toAdd = userIds.Where(id => !existingIds.Contains(id)).ToList();

        db.ApprovalAuthorityUsers.RemoveRange(toRemove);
        db.ApprovalAuthorityUsers.AddRange(toAdd.Select(id => new ApprovalAuthorityUser
        {
            ApprovalAuthorityId = authority.Id,
            UserId = id,
        }));

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(authority, await GetAssignedUsersAsync(db, authority.Id, ct)));
    }

    private static List<Guid> ParseUserIds(IReadOnlyList<string>? userIds)
    {
        var result = new List<Guid>();
        foreach (var id in userIds ?? [])
        {
            if (!Guid.TryParse(id, out var parsed))
            {
                throw new ValidationException($"'{id}' is not a valid user id.");
            }

            result.Add(parsed);
        }

        return result.Distinct().ToList();
    }

    private static async Task EnsureApproversAsync(AlasDbContext db, List<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return;
        }

        var valid = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.Role == UserRole.Approver && u.Status == UserStatus.Active)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (valid.Count != userIds.Count)
        {
            throw new ValidationException("One or more users are not active Approver users.");
        }
    }

    private static async Task<IReadOnlyList<AssignedApproverResponse>> GetAssignedUsersAsync(
        AlasDbContext db,
        Guid authorityId,
        CancellationToken ct)
    {
        var assignments = await db.ApprovalAuthorityUsers.AsNoTracking()
            .Where(x => x.ApprovalAuthorityId == authorityId)
            .ToListAsync(ct);

        var names = await LoadNamesAsync(db, assignments.Select(x => x.UserId).Distinct().ToList(), ct);
        return assignments
            .Select(x => new AssignedApproverResponse(x.UserId.ToString(), names.GetValueOrDefault(x.UserId, "Unknown")))
            .ToList();
    }

    private static async Task<Dictionary<Guid, string>> LoadNamesAsync(
        AlasDbContext db,
        List<Guid> userIds,
        CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        return await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim() })
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }

    private static ApprovalAuthorityResponse ToResponse(
        ApprovalAuthority authority,
        IReadOnlyList<AssignedApproverResponse> users) => new(
            authority.Key,
            authority.DisplayName,
            authority.Tier,
            authority.Priority,
            authority.AllowNew,
            authority.AllowRenewal,
            authority.MaxSeverity,
            authority.MaxTotalExposure,
            authority.ScopeType,
            users);
}
