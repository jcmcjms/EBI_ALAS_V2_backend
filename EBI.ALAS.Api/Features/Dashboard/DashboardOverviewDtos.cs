namespace EBI.ALAS.Api.Features.Dashboard;

/// <summary>
/// One payload for the whole dashboard page: a single round-trip per poll
/// instead of six widget-scoped requests. Lists are pre-capped server-side
/// (TOP N) so payload size is bounded regardless of portfolio size.
/// </summary>
public sealed record DashboardOverviewResponse(
    DashboardKpis Kpis,
    IReadOnlyList<PendingQueueItemDto> PendingQueue,
    IReadOnlyList<NowServingItemDto> NowServing,
    IReadOnlyList<PushBackItemDto> PushBacks,
    IReadOnlyList<ApprovedLoanItemDto> ApprovedLoans,
    IReadOnlyList<DailyTrendPointDto> WeeklyTrend,
    IReadOnlyList<DocumentQueueItemDto> DocumentQueue,
    DateTime GeneratedAtUtc);

public sealed record DashboardKpis(
    int TotalPending,
    int PendingDeltaFromYesterday,   // submissions today − submissions yesterday
    int NowServing,                  // distinct officers who acted in the last 60 min
    int PushBacksToday,
    int ApprovedToday,
    int ApprovedVsAvgPercent);       // vs 7-day daily average

public sealed record PendingQueueItemDto(
    int Position, string LamId, string BranchCode, string Status, DateTime WaitingSinceUtc,
    string ClientName, string EncoderName);

public sealed record NowServingItemDto(
    int Number, string Checker, string LamId, bool IsActive);

public sealed record PushBackItemDto(
    int Number, string LamId, string BranchCode, string Reason, DateTime PushedBackAtUtc);

public sealed record ApprovedLoanItemDto(
    string FullName, string LamId, string BranchCode, DateTime ApprovedAtUtc);

public sealed record DailyTrendPointDto(string Day, int Approved, int PushBacks);

public sealed record DocumentQueueItemDto(
    int Id, int Position, string LamId, string BranchCode, DateTime WaitingSinceUtc, int MissingCount,
    string ClientName, string EncoderName,
    string? FlaggedByName, DateTime? FlaggedAt);
