namespace Ebi.Alas.Api.Features.Users.Domain;

public sealed class User
{
    private User()
    {
        UserName = string.Empty;
        PasswordHash = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        MiddleName = string.Empty;
        Email = string.Empty;
        BranchId = string.Empty;
    }

    public Guid Id { get; private set; }

    public string UserName { get; private set; }

    public string PasswordHash { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string MiddleName { get; private set; }

    public string FullName => $"{FirstName} {MiddleName} {LastName}".Replace("  ", " ").Trim();

    public string Email { get; private set; }

    public string BranchId { get; private set; }

    public string? JobTitle { get; private set; }

    public UserRole Role { get; private set; }

    public UserStatus Status { get; private set; }

    public bool MustChangePassword { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static User Create(
        string userName,
        string passwordHash,
        string firstName,
        string lastName,
        string middleName,
        string? email,
        string branchId,
        UserRole role,
        DateTimeOffset now,
        string? jobTitle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        if (jobTitle?.Trim().Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(jobTitle), "Job title must not exceed 100 characters.");
        }

        return new User
        {
            Id = Guid.NewGuid(),
            UserName = userName.Trim(),
            PasswordHash = passwordHash,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            MiddleName = middleName?.Trim() ?? string.Empty,
            Email = email?.Trim() ?? string.Empty,
            BranchId = branchId.Trim(),
            JobTitle = NormalizeJobTitle(jobTitle),
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

    public void UpdateProfile(
        string firstName,
        string lastName,
        string middleName,
        string? email,
        string branchId,
        UserRole role,
        DateTimeOffset now,
        string? jobTitle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        if (jobTitle?.Trim().Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(jobTitle), "Job title must not exceed 100 characters.");
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        MiddleName = middleName?.Trim() ?? string.Empty;
        Email = email?.Trim() ?? string.Empty;
        BranchId = branchId.Trim();
        JobTitle = NormalizeJobTitle(jobTitle);
        Role = role;
        UpdatedAt = now;
    }

    private static string? NormalizeJobTitle(string? jobTitle) =>
        string.IsNullOrWhiteSpace(jobTitle) ? null : jobTitle.Trim();
}
