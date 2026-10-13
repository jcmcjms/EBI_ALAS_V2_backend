namespace Ebi.Alas.Api.Features.Branches;

public static class BranchMapping
{
    public static BranchResponse ToResponse(this Branch branch) => new(
        branch.Code,
        branch.Name,
        branch.AreaCode,
        branch.Status.ToString(),
        branch.CreatedAt,
        branch.UpdatedAt);
}
