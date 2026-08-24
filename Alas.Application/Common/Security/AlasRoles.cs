namespace Alas.Application.Common.Security;

public static class AlasRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string LoanManager = "LoanManager";
    public const string LoanOfficer = "LoanOfficer";
    public const string Auditor = "Auditor";

    public static readonly string[] All =
    [
        SuperAdmin,
        Admin,
        LoanManager,
        LoanOfficer,
        Auditor
    ];

    public static readonly IReadOnlyDictionary<string, string[]> Matrix =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [SuperAdmin] = [AlasPermissions.SuperAdmin],

            [Admin] =
            [
                AlasPermissions.UsersManage,
                AlasPermissions.RolesManage,
                AlasPermissions.AuditRead,
                AlasPermissions.DashboardAdmin
            ],

            [LoanManager] =
            [
                AlasPermissions.LoansRead,
                AlasPermissions.LoansCreate,
                AlasPermissions.LoansApprove,
                AlasPermissions.LoansMonitor
            ],

            [LoanOfficer] =
            [
                AlasPermissions.LoansRead,
                AlasPermissions.LoansCreate,
                AlasPermissions.LoansMonitor
            ],

            [Auditor] =
            [
                AlasPermissions.LoansRead,
                AlasPermissions.AuditRead
            ]
        };
}
