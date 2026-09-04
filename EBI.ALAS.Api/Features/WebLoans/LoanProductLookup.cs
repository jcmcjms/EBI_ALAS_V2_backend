using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("loan_product", Schema = "dbo")]
public class LoanProductLookup
{
    [Column("id_code")] public string IdCode { get; set; } = string.Empty;
    [Column("description")] public string Description { get; set; } = string.Empty;

    // Date the product was retired. NULL means the product is still active.
    // Filtered in the /api/webloans/loan-products endpoint to surface only
    // active rows. Nullable so the legacy GetLoanProductByIdCodeAsync path
    // (which fetches by id_code regardless of status) still works for
    // orphaned-product joins in the outstanding-loans view.
    [Column("expiration")] public DateTime? Expiration { get; set; }
}
