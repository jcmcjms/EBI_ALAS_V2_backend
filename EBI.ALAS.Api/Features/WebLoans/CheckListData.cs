using System.ComponentModel.DataAnnotations.Schema;
namespace EBI.ALAS.Api.Features.WebLoans;
[Table("check_list_data", Schema = "dbo")]
public class CheckListData
{
    [Column("cis_no")] public string CisNo { get; set; } = string.Empty;
    [Column("check_list_item")] public string CheckListItem { get; set; } = string.Empty;
    [Column("description")] public string? Description { get; set; }
    [Column("submitted")] public DateTime? Submitted { get; set; }
    [Column("expiration")] public DateTime? Expiration { get; set; }
    public const string LengthOfServiceItem = "CCR10";
    public const string NthpItem = "CCR07";
    public static readonly IReadOnlyList<string> CocreeItems =
        ["CCR01", "CCR02", "CCR03", "CCR04", "CCR05",
         "CCR06", "CCR07", "CCR08", "CCR09", "CCR10", "CCR11"];
}
