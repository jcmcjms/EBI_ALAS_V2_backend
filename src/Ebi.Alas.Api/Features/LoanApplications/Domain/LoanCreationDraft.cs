namespace Ebi.Alas.Api.Features.LoanApplications.Domain;

/// <summary>
/// Unsubmitted loan-creation form snapshot for one officer. The create page
/// auto-saves this as the officer types so progress survives refreshes,
/// navigation and device changes. Cleared after a successful submit or
/// explicit clear.
/// </summary>
public sealed class LoanCreationDraft
{
    public const int MaxPayloadLength = 128_000;

    private LoanCreationDraft()
    {
        Payload = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Payload { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static LoanCreationDraft Create(
        Guid userId,
        string payload,
        DateTimeOffset now)
    {
        Validate(payload);
        return new LoanCreationDraft
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Payload = payload,
            UpdatedAt = now
        };
    }

    public void Update(string payload, DateTimeOffset now)
    {
        Validate(payload);
        Payload = payload;
        UpdatedAt = now;
    }

    private static void Validate(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > MaxPayloadLength)
        {
            throw new ArgumentException(
                $"Payload must be {MaxPayloadLength} characters or fewer.",
                nameof(payload));
        }
    }
}
