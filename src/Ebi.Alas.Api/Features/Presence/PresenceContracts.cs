namespace Ebi.Alas.Api.Features.Presence;

public sealed record PresenceUserResponse(
    Guid UserId,
    string Name,
    string Role,
    string BranchCode,
    string? JobTitle,
    int Connections);
