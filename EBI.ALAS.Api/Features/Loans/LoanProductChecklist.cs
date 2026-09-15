using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Maps required checklist documents to each loan product.
/// The combination of (LoanProduct, IdCode) is the natural key —
/// each row says "product X requires checklist item Y".
/// </summary>
[Table("LoanProductChecklist")]
public class LoanProductChecklist
{
    /// <summary>
    /// Loan product code (e.g. "A16", "C35"). FK to LoanProducts.Code.
    /// </summary>
    [Column("LoanProduct")]
    public string LoanProduct { get; set; } = string.Empty;

    /// <summary>
    /// Checklist item code from webloan's check_list_all (e.g. "A1004", "CCR38").
    /// </summary>
    [Column("IdCode")]
    public string IdCode { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property to the parent loan product.
    /// </summary>
    public LoanProduct? Product { get; set; }
}
