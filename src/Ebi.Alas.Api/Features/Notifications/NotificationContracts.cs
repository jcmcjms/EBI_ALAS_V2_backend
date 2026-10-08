namespace Ebi.Alas.Api.Features.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    string Title,
    string Body,
    string Type,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);
