namespace EBI.ALAS.Api.Features.Branches;
public sealed class Branch
{
    public int Id { get; init; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? AreaCode { get; set; }
}
