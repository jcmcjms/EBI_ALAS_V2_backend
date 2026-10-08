namespace Ebi.Alas.Api.Features.Workflow;

public enum WorkflowStage
{
    Recommendation = 1,
    Evaluation = 2,
    Approval = 3
}

public enum QueueItemState
{
    Queued = 1,
    Active = 2,
    Completed = 3
}
