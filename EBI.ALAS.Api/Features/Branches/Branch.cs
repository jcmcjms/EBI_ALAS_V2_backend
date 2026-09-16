namespace EBI.ALAS.Api.Features.Branches;
public class Branch
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty; // Branch code (e.g., "001", "HO")
    public string Name { get; set; } = string.Empty; // Branch display name (e.g., "Lianga Branch")
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Area grouping for Area Head scope (e.g. "A1".."A4"). Null for Head Office.</summary>
    public string? AreaCode { get; set; }
}