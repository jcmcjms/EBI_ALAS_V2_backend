namespace EBI.ALAS.Api.Common.Constants;
public static class RolePermissions
{
    private static readonly Dictionary<string, string[]> RolePermissionMap = new()
    {
        [Roles.Encoder] = new[]
        {
            Permissions.LoansCreate,
            Permissions.LoansView
        },
        [Roles.Recommender] = new[]
        {
            Permissions.LoansRecommend,
            Permissions.LoansView
        },
        [Roles.Evaluator] = new[]
        {
            Permissions.LoansEvaluate,
            Permissions.LoansView
        },
        [Roles.Approver] = new[]
        {
            Permissions.LoansApprove,
            Permissions.LoansReject,
            Permissions.LoansView
        },
        [Roles.Admin] = new[]
        {
            // Loan permissions
            Permissions.LoansCreate,
            Permissions.LoansView,
            Permissions.LoansRecommend,
            Permissions.LoansEvaluate,
            Permissions.LoansApprove,
            Permissions.LoansReject,
            // Loan product management permissions
            Permissions.LoanProductManage,
            Permissions.LoanProductView,
            // User management permissions
            Permissions.UserCreate,
            Permissions.UserView,
            Permissions.UserEdit,
            Permissions.UserSuspend,
            // Role & permission management permissions
            Permissions.RoleManage,
            Permissions.RoleView,
            // Audit log permissions
            Permissions.AuditLogsView
        }
    };
    public static string[] GetPermissionsForRole(string role)
    {
        return RolePermissionMap.TryGetValue(role, out var permissions)
            ? permissions
            : Array.Empty<string>();
    }
    public static bool RoleHasPermission(string role, string permission)
    {
        if (!RolePermissionMap.TryGetValue(role, out var permissions))
            return false;

        // Admin wildcard check
        if (role == Roles.Admin)
            return true;

        return permissions.Contains(permission);
    }
}
