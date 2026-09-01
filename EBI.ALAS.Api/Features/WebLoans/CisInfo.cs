using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;
[Table("cis_info", Schema = "dbo")]
public class CisInfo
{
    [Column("cis_no")] public string CisNo { get; set; } = string.Empty;
    [Column("fname")] public string FirstName { get; set; } = string.Empty;
    [Column("mname")] public string? MiddleName { get; set; }
    [Column("lname")] public string LastName { get; set; } = string.Empty;
    [Column("title")] public string? Title { get; set; }
    [Column("appelation")] public string? Appelation { get; set; }

    // Stored as varchar(10) in webloan — parse defensively.
    [Column("p_bday")] public string? BirthDateRaw { get; set; }

    // Home address components
    [Column("h_sadd")] public string? HouseStreet { get; set; }
    [Column("h_barangay")] public string? Barangay { get; set; }
    [Column("h_village")] public string? Village { get; set; }
    [Column("h_city")] public string? City { get; set; }
    [Column("h_state_prov")] public string? StateProvince { get; set; }
    [Column("h_zip")] public string? Zip { get; set; }

    // Employment / agency
    [Column("b_comp")] public string? Company { get; set; }
    [Column("company_type")] public byte? CompanyTypeCode { get; set; }
    [Column("occupation")] public string? Occupation { get; set; }
    [Column("b_jtitle")] public string? JobTitle { get; set; }
    [Column("b_dept")] public string? Department { get; set; }
    [Column("b_region_code")] public string? RegionCode { get; set; }
    [Column("b_division_code")] public string? DivisionCode { get; set; }
    [Column("b_station_code")] public string? StationCode { get; set; }
    [Column("b_employee_no")] public string? EmployeeNo { get; set; }

    [Column("bk")] public string? BankCode { get; set; }
    [Column("bch")] public string? BranchCode { get; set; }
}
