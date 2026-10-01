namespace EBI.ALAS.Api.Features.Loans;
public record QueuePositionInfo(
    QueueStage Stage,
    int Position,
    int QueueLength,
    int? OwnerUserId,
    string? OwnerName,
    bool IsHead);
public record DeskQueueResponse(
    string DeskLabel,
    IReadOnlyList<QueuedLoanDto> Items,
    QueuedLoanDto? CurrentClaim,
    string ScopeDescription);
public record QueuedLoanDto(
    int LoanId,
    string LamId,
    string ClientName,
    int Position,
    bool IsHead,
    int? OwnerUserId,
    string? OwnerName,
    DateTime EnqueuedAt,
    string Status,
    string BranchCode,
    string ProductCode,
    string Product,
    string? LoanType,
    string? Purpose,
    decimal ProposedAmount,
    int TermDays,
    DateTime ApplicationDate,
    bool HasDeviations);
public record ClaimResponse(
    int LoanId,
    string LamId,
    string ClientName,
    string Status,
    DateTime LeasedAt);
public record LoanQueueState(
    int Position,
    bool IsHead,
    int? OwnerUserId,
    string? OwnerName,
    bool IsMine,
    DateTime? LeasedAt);
public abstract record ClaimByIdResult
{
    public sealed record Claimed(ClaimResponse Response) : ClaimByIdResult;
    public sealed record NotHead() : ClaimByIdResult;
    public sealed record LeasedByOther(string OwnerName) : ClaimByIdResult;
    public sealed record NotFound() : ClaimByIdResult;
}
public interface IWorkflowQueueService
{
    Task EnqueueAsync(LoanApplication loan, string newStatus, CancellationToken ct);
    Task DequeueAndPromoteAsync(LoanApplication loan, string oldStatus, CancellationToken ct);
    Task<IReadOnlyDictionary<int, QueuePositionInfo>> GetPositionsAsync(
        IReadOnlyCollection<int> loanIds, CancellationToken ct);
    Task<bool> IsHeadOwnerAsync(int loanId, int userId, string currentStatus, CancellationToken ct);
    Task<ClaimResponse?> ClaimHeadAsync(int userId, string role, string branchCode, CancellationToken ct);
    Task<DeskQueueResponse> GetDeskAsync(int userId, string role, string branchCode, CancellationToken ct);
    Task<bool> ReleaseClaimAsync(int userId, CancellationToken ct);
    Task<ClaimByIdResult> ClaimByIdAsync(int id, int userId, string role, string branchCode, CancellationToken ct);
    Task PromoteHeadAsync(string partitionKey, CancellationToken ct);
    Task<LoanQueueState?> GetQueueStateAsync(int loanId, int userId, string currentStatus, CancellationToken ct);
    Task ExtendLeaseAsync(int loanId, int userId, CancellationToken ct);
}
