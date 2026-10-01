namespace EBI.ALAS.Api.Features.Branches;
public sealed record BranchResponse(
    int Id,
    string Code,
    string Name,
    bool IsActive,
    DateTime CreatedAt);
public sealed record BranchListResponse(
    int Id,
    string Code,
    string Name,
    bool IsActive);
