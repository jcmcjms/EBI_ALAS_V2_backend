namespace Alas.Application.Common.Security;

public static class AlasPermissions
{
    public const string LoansRead = "loans.read";
    public const string LoansWrite = "loans.write";
    public const string LoansApprove =  "loans.approve";
    public const string LoansDisburse = "loans.disburse";
    
    public const string ReportsRead = "reports.read";
    public const string AuditRead = "audits.read";
    
    public const string UsersManage =  "users.manage";
    public const string RolesManage = "roles.manage";

    public static readonly string[] All =
    [
        LoansRead,
        LoansWrite,
        LoansApprove,
        LoansDisburse,
        ReportsRead,
        AuditRead,
        UsersManage,
        RolesManage
    ];
}