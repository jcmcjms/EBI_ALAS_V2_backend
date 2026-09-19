namespace EBI.ALAS.Api.Features.Branches;

/// <summary>
/// Branch response DTO. Immutable record.
/// </summary>
public sealed record BranchResponse(
    int Id,
    string Code,
    string Name,
    bool IsActive,
    DateTime CreatedAt);

/// <summary>
/// Branch list response DTO (lightweight). Immutable record.
/// </summary>
public sealed record BranchListResponse(
    int Id,
    string Code,
    string Name,
    bool IsActive);
