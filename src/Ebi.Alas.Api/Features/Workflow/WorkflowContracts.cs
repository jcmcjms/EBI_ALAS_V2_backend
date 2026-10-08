namespace Ebi.Alas.Api.Features.Workflow;

public sealed record ClaimQueueResponse(
    Guid Id,
    Guid LoanApplicationId,
    string Stage,
    string State,
    DateTimeOffset? LeasedAt);
