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

public sealed record DeskQueueResult(
    string DeskLabel,
    IReadOnlyList<DeskQueueItemDto> Items,
    DeskQueueItemDto? CurrentClaim,
    string ScopeDescription);

public sealed record DeskClaimResult(
    Guid LoanId,
    string LamId,
    string ClientName,
    string Status,
    DateTimeOffset LeasedAt);
