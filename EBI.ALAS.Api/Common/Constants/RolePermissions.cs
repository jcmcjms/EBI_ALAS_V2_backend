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
            Permissions.LoansCreate,
            Permissions.LoansView,
            Permissions.LoansRecommend,
            Permissions.LoansEvaluate,
            Permissions.LoansApprove,
            Permissions.LoansReject,
            Permissions.LoanProductManage,
            Permissions.LoanProductView,
            Permissions.UserCreate,
            Permissions.UserView,
            Permissions.UserEdit,
            Permissions.UserSuspend,
            Permissions.RoleManage,
            Permissions.RoleView,
            Permissions.AuditLogsView,
            Permissions.WorkflowManage
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

        if (role == Roles.Admin)
            return true;

        return permissions.Contains(permission);
    }
}
