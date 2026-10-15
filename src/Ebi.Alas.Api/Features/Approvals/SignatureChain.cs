namespace Ebi.Alas.Api.Features.Approvals;

public enum SignatureSlotStatus
{
    Pending = 1,
    Signed = 2,
    Skipped = 3
}

public sealed class SignatureChainEntry
{
    private SignatureChainEntry()
    {
        RoleName = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid LoanApplicationId { get; private set; }

    public int Order { get; private set; }

    public string RoleName { get; private set; }

    public Guid? UserId { get; private set; }

    public SignatureSlotStatus Status { get; private set; }

    public DateTimeOffset? SignedAt { get; private set; }

    /// <summary>Signer's ink (base64 data URL) captured at signing time. Immutable after Sign.</summary>
    public string? SignatureImage { get; private set; }

    public static SignatureChainEntry Create(
        Guid loanApplicationId,
        int order,
        string roleName,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        return new SignatureChainEntry
        {
            Id = Guid.NewGuid(),
            LoanApplicationId = loanApplicationId,
            Order = order,
            RoleName = roleName.Trim(),
            Status = SignatureSlotStatus.Pending
        };
    }

    public void Sign(Guid userId, DateTimeOffset now, string? signatureImage)
    {
        if (Status == SignatureSlotStatus.Signed)
        {
            return;
        }

        UserId = userId;
        Status = SignatureSlotStatus.Signed;
        SignedAt = now;
        SignatureImage = Users.Domain.ESignaturePolicy.Sanitize(signatureImage);
    }

    /// <summary>
    /// Attach ink to a slot that was signed without a stored snapshot
    /// (profile signature captured after the workflow action).
    /// </summary>
    public void AttachSignatureImageIfMissing(string? signatureImage)
    {
        if (Status != SignatureSlotStatus.Signed || SignatureImage is not null)
        {
            return;
        }

        SignatureImage = Users.Domain.ESignaturePolicy.Sanitize(signatureImage);
    }
}

public sealed record SlaPolicy(int RecommendationHours, int EvaluationHours, int ApprovalHours)
{
    public static readonly SlaPolicy Default = new(24, 48, 72);
}
