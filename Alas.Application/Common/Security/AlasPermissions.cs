namespace Alas.Application.Common.Security;

public static class AlasPermissions
{
    public const string LoansRead = "loans.read";
    public const string LoansCreate = "loans.create";
    public const string LoansApprove = "loans.approve";
    public const string LoansMonitor = "loans.monitor";

    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    public const string AuditRead = "audit.read";
    public const string DashboardAdmin = "dashboard.admin";

    public const string SuperAdmin = "*";

    public static readonly string[] All =
    [
        LoansRead,
        LoansCreate,
        LoansApprove,
        LoansMonitor,
        UsersManage,
        RolesManage,
        AuditRead,
        DashboardAdmin
    ];

    /// <summary>
    /// Temporary compatibility map for legacy frontend permission strings.
    /// Remove after frontend is fully migrated to canonical permissions.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyToCanonical =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["user.create"] = UsersManage,
            ["user.manage"] = UsersManage,
            ["role.manage"] = RolesManage,
            ["roles.manage"] = RolesManage,
            ["loans.view"] = LoansRead,
            ["loan.read"] = LoansRead,
            ["loan.create"] = LoansCreate,
            ["loan.approve"] = LoansApprove,
            ["loan.monitor"] = LoansMonitor,
            ["loans.write"] = LoansCreate,
            ["loans.disburse"] = LoansMonitor,
            ["reports.read"] = DashboardAdmin,
            ["audits.read"] = AuditRead
        };

    public static string Normalize(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return LegacyToCanonical.TryGetValue(permission, out var canonical)
            ? canonical
            : permission;
    }

    public static bool IsKnown(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        if (permission == SuperAdmin)
        {
            return true;
        }

        return All.Contains(permission, StringComparer.Ordinal);
    }
}
