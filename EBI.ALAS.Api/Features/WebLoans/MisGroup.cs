using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Maps to dbo.mis_group in the WebLoan database — a multi-purpose hierarchical
/// lookup table. Each row belongs to a logical group identified by <c>group_no</c>:
/// <list type="bullet">
///   <item><c>group_no = 1</c> — Region/area groupings (path/description lookup).</item>
///   <item><c>group_no = 2</c> — Account officers / requesting officers. <c>path</c>
///     is referenced by <c>loan_acct_info.solicitor</c>; the resolved <c>description</c>
///     is the requesting officer's name (e.g. "ALDREX JOEY L. CEZAR").</item>
///   <item><c>group_no = 14</c> — Agency type. <c>id_code</c> is referenced by
///     <c>cis_info_misc_data.value_str</c> when <c>id_code = 14</c>.</item>
///   <item>Other <c>group_no</c> values cover other categorizations (industry,
///     economic activity, debt instrument, etc.).</item>
/// </list>
/// Hierarchy is materialized via <c>path</c> (slash-delimited ancestor chain) and
/// <c>grp_level</c> (0 = root, increasing depth).
/// </summary>
[Table("mis_group", Schema = "dbo")]
public class MisGroup
{
    [Column("frp_id")] public int FrpId { get; set; }

    /// <summary>Logical group identifier — selects which classification the row belongs to.</summary>
    [Column("group_no")] public int GroupNo { get; set; }

    /// <summary>Parent id_code within the same group_no (null at root).</summary>
    [Column("pid_code")] public string? ParentIdCode { get; set; }

    /// <summary>Own identifier within the group_no.</summary>
    [Column("id_code")] public string IdCode { get; set; } = string.Empty;

    /// <summary>0 = root, increasing as we descend the hierarchy.</summary>
    [Column("grp_level")] public int? GrpLevel { get; set; }

    /// <summary>Human-readable name of the node (e.g. officer name for group_no=2).</summary>
    [Column("description")] public string? Description { get; set; }

    /// <summary>Secondary description (rarely populated).</summary>
    [Column("description2")] public string? Description2 { get; set; }

    /// <summary>
    /// Slash-delimited ancestor chain including the row's own id_code
    /// (e.g. <c>"CLD/C00/C0001/C000101"</c> for the requesting officer row).
    /// Matched directly against <c>loan_acct_info.solicitor</c> and
    /// <c>loan_acct_info.cat_mis_group2</c>.
    /// </summary>
    [Column("path")] public string? Path { get; set; }
}
