namespace EBI.ALAS.Api.Features.Loans.DTOs;
public sealed record TimelineEventDto
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public string? ActorName { get; init; }
    public string? ActorRole { get; init; }
    public string? Action { get; init; }
    public string? FromStatus { get; init; }
    public string? ToStatus { get; init; }
    public string? Comment { get; init; }
    public string? Subject { get; init; }
    public string? SubjectCode { get; init; }
}
