namespace Ebi.Alas.Api.Features.Users.Domain;

public sealed class User
{
    private User()
    {
        UserName = string.Empty;
        PasswordHash = string.Empty;
        FullName = string.Empty;
        Email = string.Empty;
        BranchId = string.Empty;
    }

    public Guid Id { get; private set; }

    public string UserName { get; private set; }

    public string PasswordHash { get; private set; }

    public string FullName { get; private set; }

    public string Email { get; private set; }

    public string BranchId { get; private set; }

    public UserRole Role { get; private set; }

    public UserStatus Status { get; private set; }

    public bool MustChangePassword { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static User Create(
        string userName,
        string passwordHash,
        string fullName,
        string? email,
        string branchId,
        UserRole role,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        return new User
        {
            Id = Guid.NewGuid(),
            UserName = userName.Trim(),
            PasswordHash = passwordHash,
            FullName = fullName.Trim(),
            Email = email?.Trim() ?? string.Empty,
            BranchId = branchId.Trim(),
            Role = role,
            Status = UserStatus.Active,
            MustChangePassword = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Suspend(DateTimeOffset now)
    {
        if (Status == UserStatus.Suspended)
        {
            throw new InvalidOperationException("User is already suspended.");
        }

        Status = UserStatus.Suspended;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        if (Status == UserStatus.Active)
        {
            return;
        }

        Status = UserStatus.Active;
        UpdatedAt = now;
    }

    public void SetPasswordHash(string passwordHash, bool mustChangePassword, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        UpdatedAt = now;
    }

    public void ClearMustChangePassword(DateTimeOffset now)
    {
        MustChangePassword = false;
        UpdatedAt = now;
    }

    public void UpdateProfile(string fullName, string? email, string branchId, UserRole role, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        FullName = fullName.Trim();
        Email = email?.Trim() ?? string.Empty;
        BranchId = branchId.Trim();
        Role = role;
        UpdatedAt = now;
    }
}
