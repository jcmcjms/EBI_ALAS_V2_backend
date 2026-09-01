using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("mis_group", Schema = "dbo")]
public class MisGroup
{
    [Column("frp_id")] public int FrpId { get; set; }

    [Column("group_no")] public int GroupNo { get; set; }

    [Column("pid_code")] public string? ParentIdCode { get; set; }

    [Column("id_code")] public string IdCode { get; set; } = string.Empty;

    [Column("grp_level")] public int? GrpLevel { get; set; }

    [Column("description")] public string? Description { get; set; }

    [Column("description2")] public string? Description2 { get; set; }
    [Column("path")] public string? Path { get; set; }
}
