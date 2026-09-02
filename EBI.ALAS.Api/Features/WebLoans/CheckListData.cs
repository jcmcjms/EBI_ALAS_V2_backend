using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("check_list_data", Schema = "dbo")]
public class CheckListData
{
    // EAV-style attribute store: same column names carry different meanings
    // depending on the (cis_no, check_list_item) pair. Concrete semantics
    // currently used by this service:
    //
    //   check_list_item = 'CCR10' → description is hire date (varchar)
    //                                  expiration is contract-end date
    //   check_list_item = 'CCR07' → description is NTHP amount (varchar number)
    //                                  expiration is NTHP date
    //
    // description and expiration are kept as nullable strings — never try
    // to parse centrally; let the caller decode per item code.
    [Column("cis_no")] public string CisNo { get; set; } = string.Empty;
    [Column("check_list_item")] public string CheckListItem { get; set; } = string.Empty;
    [Column("description")] public string? Description { get; set; }
    [Column("expiration")] public DateTime? Expiration { get; set; }

    // Item codes referenced by this service. Centralized here so the
    // semantic-table-of-this-EAV-store is documented in one place.
    public const string LengthOfServiceItem = "CCR10";
    public const string NthpItem = "CCR07";
}