using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("loan_product", Schema = "dbo")]
public class LoanProductLookup
{
    [Column("id_code")] public string IdCode { get; set; } = string.Empty;
    [Column("description")] public string Description { get; set; } = string.Empty;

    // Date the product was retired. NULL means the product is still active.
// Filtered in the /api/webloans/loan-products endpoint to surface only
// active rows. Read by the loan-product sync service (LoanProductSyncService)
// so it can mirror webloan's active/retired state into the ALAS-owned
// loan_product mirror table. The outstanding-loans endpoint handles the
// orphaned-product case (a loan referencing a retired product code) with
// a SQL LEFT JOIN + ISNULL coercion, not via this entity.
[Column("expiration")] public DateTime? Expiration { get; set; }
}
