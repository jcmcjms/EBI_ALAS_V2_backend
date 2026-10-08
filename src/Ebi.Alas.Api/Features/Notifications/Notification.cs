namespace Ebi.Alas.Api.Features.Notifications;

public enum NotificationType
{
    Application = 1,
    Action = 2,
    Message = 3,
    System = 4
}

public sealed class Notification
{
    private Notification()
    {
        Title = string.Empty;
        Body = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public NotificationType Type { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(
        Guid userId,
        string title,
        string body,
        NotificationType type,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title.Trim(),
            Body = body.Trim(),
            Type = type,
            CreatedAt = now
        };
    }

    public void MarkRead(DateTimeOffset now)
    {
        ReadAt ??= now;
    }
}
