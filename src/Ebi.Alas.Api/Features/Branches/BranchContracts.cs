namespace Ebi.Alas.Api.Features.Branches;

public sealed record BranchResponse(string Code, string Name, string AreaCode, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateBranchRequest(string Code, string Name, string AreaCode);

public sealed record UpdateBranchRequest(string Name, string AreaCode);

public sealed record UpdateBranchStatusRequest(string Status);
