using System.ComponentModel.DataAnnotations.Schema;
using EBI.ALAS.Api.Features.Auth;
namespace EBI.ALAS.Api.Features.Loans;
[Table("LoanProducts")]
public class LoanProduct
{
    [Column("Code")]
    public string Code { get; set; } = string.Empty;
    [Column("Description")]
    public string Description { get; set; } = string.Empty;
    [Column("MinAmount")]
    public decimal MinAmount { get; set; }
    [Column("MaxAmount")]
    public decimal MaxAmount { get; set; }
    [Column("MinTermDays")]
    public int MinTermDays { get; set; }
    [Column("MaxTermDays")]
    public int MaxTermDays { get; set; }
    [Column("NotarialFee")]
    public decimal NotarialFee { get; set; }
    [Column("DocStampFee")]
    public decimal DocStampFee { get; set; }
    [Column("InsuranceFee")]
    public decimal InsuranceFee { get; set; }
    [Column("AdvanceInterestRate")]
    public decimal AdvanceInterestRate { get; set; }
    [Column("ApplicationChargeRate")]
    public decimal ApplicationChargeRate { get; set; }
    [Column("AmortizationMode")]
    public string AmortizationMode { get; set; } = "DIM";
    [Column("ChargeAdvanceInterest")]
    public bool ChargeAdvanceInterest { get; set; }
    [Column("IsRetired")]
    public bool IsRetired { get; set; }
    [Column("LastSyncedAt")]
    public DateTime LastSyncedAt { get; set; }
    [Column("UpdatedDate")]
    public DateTime UpdatedDate { get; set; }
    [Column("UpdatedById")]
    public int? UpdatedById { get; set; }
    public User? UpdatedBy { get; set; } = null!;
}
