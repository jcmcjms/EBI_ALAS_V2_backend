using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Maps to dbo.cis_info_misc_data in the WebLoan database — per-client miscellaneous
/// attribute rows keyed by an <c>id_code</c>. A single client can have many rows,
/// one per attribute. Different <c>id_code</c> values carry different value shapes:
/// <list type="bullet">
///   <item><c>id_code = 14</c> — Agency type code. <c>value_str</c> matches
///     <c>mis_group.id_code</c> (in group_no=14) and resolves to the agency
///     type description (e.g. "RPSU", "GOVERNMENT", etc.).</item>
/// </list>
/// Other <c>id_code</c> values exist in the source table; only those needed by
/// ALAS are documented here. Add new <see cref="IdCode"/> constants as ALAS
/// requirements grow.
/// </summary>
[Table("cis_info_misc_data", Schema = "dbo")]
public class CisInfoMiscData
{
    [Column("cis_no")] public string CisNo { get; set; } = string.Empty;

    /// <summary>Attribute code. <see cref="AgencyTypeIdCode"/> for the agency type.</summary>
    [Column("id_code")] public int IdCode { get; set; }

    /// <summary>String payload — used for code-valued attributes (e.g. agency type).</summary>
    [Column("value_str")] public string? ValueStr { get; set; }

    /// <summary>Date payload — used for date-valued attributes.</summary>
    [Column("value_date")] public DateTime? ValueDate { get; set; }

    /// <summary>Integer payload.</summary>
    [Column("value_int")] public int? ValueInt { get; set; }

    /// <summary>Numeric payload.</summary>
    [Column("value_numeric")] public decimal? ValueNumeric { get; set; }

    /// <summary>cis_info_misc_data id_code for the agency type attribute.</summary>
    public const int AgencyTypeIdCode = 14;
}
