namespace EBI.ALAS.Api.Features.Branches;

/// <summary>
/// Branch entity representing a bank branch.
/// </summary>
public class Branch
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty; // Branch code (e.g., "001", "HO")
    public string Name { get; set; } = string.Empty; // Branch display name (e.g., "Lianga Branch")
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}