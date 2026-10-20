using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Features.Workflow;

public sealed record DeskQueueItemDto(
    Guid LoanId,
    string LamId,
    string ClientName,
    int Position,
    bool IsHead,
    Guid? OwnerUserId,
    string? OwnerName,
    DateTimeOffset EnqueuedAt,
    string Status,
    string BranchCode,
    string ProductCode,
    string Product,
    string? LoanType,
    string? Purpose,
    decimal ProposedAmount,
    int TermDays,
    DateTimeOffset ApplicationDate,
    bool HasDeviations);

/// <summary>
/// A client's bundle of applications waiting at this desk. The desk serves one
/// client at a time; claiming takes every queued application in the bundle.
/// </summary>
public sealed record DeskQueueClientDto(
    string GroupNo,
    string ClientName,
    int Position,
    bool IsHead,
    Guid? OwnerUserId,
    string? OwnerName,
    DateTimeOffset EnqueuedAt,
    IReadOnlyList<DeskQueueItemDto> Applications);

public sealed record DeskQueueResult(
    string DeskLabel,
    IReadOnlyList<DeskQueueClientDto> Items,
    DeskQueueClientDto? CurrentClaim,
    string ScopeDescription);

public sealed record DeskClaimApplicationDto(
    Guid LoanId,
    string LamId,
    string Status);

public sealed record DeskClaimResult(
    Guid LoanId,
    string LamId,
    string ClientName,
    string Status,
    DateTimeOffset LeasedAt,
    string GroupNo,
    IReadOnlyList<DeskClaimApplicationDto> Applications);
