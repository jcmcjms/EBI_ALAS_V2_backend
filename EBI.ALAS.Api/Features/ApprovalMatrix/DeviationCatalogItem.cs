namespace EBI.ALAS.Api.Features.ApprovalMatrix;

/// <summary>
/// Seeded deviation catalog; the FE deviations section renders from it.
/// Each deviation reason maps to a severity level that drives routing.
/// </summary>
public class DeviationCatalogItem
{
    public int Id { get; set; }                          // PK
    public string Description { get; set; } = null!;     // e.g. "Age not within the prescribed parameters"
    public DeviationSeverity Severity { get; set; }      // None, Minor, or Major
}
