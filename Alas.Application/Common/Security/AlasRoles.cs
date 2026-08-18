namespace Alas.Application.Common.Security;

public static class AlasRoles
{
    public const string Admin = "Admin";
    public const string Auditor = "Auditor";
    public const string Approver = "Approver";

    public static readonly Dictionary<string, string[]> Matrix = new()
    {
        [Admin] = AlasPermissions.All,

        [Auditor] =
        [
            AlasPermissions.LoansRead,
            AlasPermissions.ReportsRead,
            AlasPermissions.AuditRead
        ],

        [Approver] =
        [
            AlasPermissions.LoansRead,
            AlasPermissions.LoansApprove
        ]
    };
}