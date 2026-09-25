namespace EBI.ALAS.Api.Features.Loans;

public enum WorkflowAction
{
    Recommend,
    NotRecommend,
    PushBack,
    Approve,
    Reject,
    ReturnForRevision,
}

public sealed record ResolvedAction(string TargetStatus, string? Verdict);