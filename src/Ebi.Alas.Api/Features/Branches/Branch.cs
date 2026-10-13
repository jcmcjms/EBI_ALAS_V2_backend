namespace Ebi.Alas.Api.Features.Branches;

public sealed class Branch
{
    private Branch()
    {
        Code = string.Empty;
        Name = string.Empty;
        AreaCode = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string AreaCode { get; private set; }

    public BranchStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Branch Create(string code, string name, string areaCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(areaCode);
        if (code.Trim().Length > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(code), "Code must be at most 8 characters.");
        }

        if (name.Trim().Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "Name must be at most 200 characters.");
        }

        if (areaCode.Trim().Length > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(areaCode), "AreaCode must be at most 16 characters.");
        }

        return new Branch
        {
            Id = Guid.NewGuid(),
            Code = code.Trim(),
            Name = name.Trim(),
            AreaCode = areaCode.Trim(),
            Status = BranchStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateDetails(string name, string areaCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(areaCode);
        if (name.Trim().Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "Name must be at most 200 characters.");
        }

        if (areaCode.Trim().Length > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(areaCode), "AreaCode must be at most 16 characters.");
        }

        Name = name.Trim();
        AreaCode = areaCode.Trim();
        UpdatedAt = now;
    }

    public void ChangeStatus(BranchStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }
}
