namespace EBI.ALAS.Api.Features.Branches;

/// <summary>
/// Branch entity. Represents a bank branch location.
/// EF Core entity — uses init setters for immutability after construction.
/// </summary>
public sealed class Branch
{
    public int Id { get; init; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Area grouping for Area Head scope (e.g. "A1".."A4"). Null for Head Office.</summary>
    public string? AreaCode { get; set; }
}
