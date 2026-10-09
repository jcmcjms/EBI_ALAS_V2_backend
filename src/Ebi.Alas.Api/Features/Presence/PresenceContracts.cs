namespace Ebi.Alas.Api.Features.Presence;

/// <summary>API row for GET /api/presence/online (raw array, no envelope).</summary>
public sealed record PresenceUserResponse(
    Guid UserId,
    string Name,
    string Role,
    string BranchCode,
    string? JobTitle,
    int Connections);
