namespace EBI.ALAS.Api.Features.ApprovalMatrix;
public class DeviationCatalogItem
{
    public int Id { get; set; }
    public string Description { get; set; } = null!;
    public DeviationSeverity Severity { get; set; }
}
