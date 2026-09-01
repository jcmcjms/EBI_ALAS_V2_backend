using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("cis_info_misc_data", Schema = "dbo")]
public class CisInfoMiscData
{
    [Column("cis_no")] public string CisNo { get; set; } = string.Empty;

    [Column("id_code")] public int IdCode { get; set; }

    [Column("value_str")] public string? ValueStr { get; set; }

    [Column("value_date")] public DateTime? ValueDate { get; set; }

    [Column("value_int")] public int? ValueInt { get; set; }

    [Column("value_numeric")] public decimal? ValueNumeric { get; set; }

    public const int AgencyTypeIdCode = 14;
}
