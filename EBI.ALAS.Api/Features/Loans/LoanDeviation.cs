namespace EBI.ALAS.Api.Features.Loans;
public class LoanDeviation
{
    public int Id { get; set; }
    public int LoanApplicationId { get; set; }
    public LoanApplication LoanApplication { get; set; } = null!;
    public string ReasonText { get; set; } = string.Empty;
    public string EncoderJustification { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsFeeOverride { get; set; }
    public ICollection<DeviationRemark> Remarks { get; set; } = new List<DeviationRemark>();
    public const string FeeOverrideReason = "Fee override (notarial / doc stamps / insurance)";
}
