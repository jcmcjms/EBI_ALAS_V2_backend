namespace EBI.ALAS.Api.Features.Branches;

public record BranchResponse(
    int Id,
    string Code,
    string Name,
    bool IsActive,
    DateTime CreatedAt
);

public record BranchListResponse(
    int Id,
    string Code,
    string Name,
    bool IsActive
);