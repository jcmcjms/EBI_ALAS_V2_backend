namespace EBI.ALAS.Api.Common.Constants;

/// <summary>
/// Permission constants for granular access control.
/// </summary>
public static class Permissions
{
    // Loan permissions
    public const string LoansCreate = "loans.create";
    public const string LoansView = "loans.view";
    public const string LoansRecommend = "loans.recommend";
    public const string LoansEvaluate = "loans.evaluate";
    public const string LoansApprove = "loans.approve";
    public const string LoansReject = "loans.reject";

    // User management permissions
    public const string UserCreate = "user.create";
    public const string UserView = "user.view";
    public const string UserEdit = "user.edit";
    public const string UserSuspend = "user.suspend";

    // Role & permission management permissions
    public const string RoleManage = "role.manage";
    public const string RoleView = "role.view";

    /// <summary>
    /// All available permissions in the system.
    /// </summary>
    public static readonly string[] All = new[]
    {
        LoansCreate,
        LoansView,
        LoansRecommend,
        LoansEvaluate,
        LoansApprove,
        LoansReject,
        UserCreate,
        UserView,
        UserEdit,
        UserSuspend,
        RoleManage,
        RoleView
    };
}
