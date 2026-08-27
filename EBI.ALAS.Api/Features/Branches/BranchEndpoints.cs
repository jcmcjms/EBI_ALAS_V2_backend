using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Branches;

public static class BranchEndpoints
{
    public static void MapBranchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/branches").WithTags("Branches");

        group.MapGet("/", async (
            IBranchService branchService,
            int pageNumber = 1,
            int pageSize = 50,
            bool? isActive = null) =>
        {
            var result = await branchService.GetBranchesAsync(pageNumber, pageSize, isActive);
            return Results.Ok(ApiResponse<PagedResult<BranchListResponse>>.SuccessResponse(result));
        }).WithName("GetBranches").RequireAuthorization("CanViewUsers");

        group.MapGet("/all", async (
            IBranchService branchService,
            bool? isActive = null) =>
        {
            var result = await branchService.GetAllBranchesAsync(isActive);
            return Results.Ok(ApiResponse<IReadOnlyList<BranchListResponse>>.SuccessResponse(result));
        }).WithName("GetAllBranches").RequireAuthorization("CanViewUsers");

        group.MapGet("/{id:int}", async (int id, IBranchService branchService) =>
        {
            var branch = await branchService.GetByIdAsync(id);
            return branch is null
                ? Results.NotFound(ApiResponse.ErrorResponse("Branch not found"))
                : Results.Ok(ApiResponse<BranchResponse>.SuccessResponse(branch));
        }).WithName("GetBranchById").RequireAuthorization("CanViewUsers");

        group.MapGet("/code/{code}", async (string code, IBranchService branchService) =>
        {
            var branch = await branchService.GetByCodeAsync(code);
            return branch is null
                ? Results.NotFound(ApiResponse.ErrorResponse("Branch not found"))
                : Results.Ok(ApiResponse<BranchResponse>.SuccessResponse(branch));
        }).WithName("GetBranchByCode").RequireAuthorization("CanViewUsers");
    }
}