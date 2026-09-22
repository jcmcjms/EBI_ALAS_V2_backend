namespace EBI.ALAS.Api.Features.Loans.DTOs;

/// <summary>
/// One entry of a loan's unified history. Structured, not pre-sentenced:
/// the frontend owns the plain-language vocabulary (single source of truth,
/// testable, localizable) while this DTO keeps the audit-grade facts
/// (raw action codes, statuses, actors, timestamps) intact.
/// </summary>
public sealed record TimelineEventDto
{
    /// <summary>Stable client key, e.g. "workflow:12", "deviation:4".</summary>
    public required string Id { get; init; }

    /// <summary>workflow | deviation | deviationRemark | documentRemark | remark</summary>
    public required string Type { get; init; }

    public DateTime OccurredAtUtc { get; init; }
    public string? ActorName { get; init; }
    public string? ActorRole { get; init; }

    // Workflow facts (raw, for audit integrity).
    public string? Action { get; init; }
    public string? FromStatus { get; init; }
    public string? ToStatus { get; init; }

    /// <summary>Free text: action comment, justification, or remark body.</summary>
    public string? Comment { get; init; }

    /// <summary>Human-readable subject: deviation reason or checklist name.</summary>
    public string? Subject { get; init; }
    public string? SubjectCode { get; init; }
}
