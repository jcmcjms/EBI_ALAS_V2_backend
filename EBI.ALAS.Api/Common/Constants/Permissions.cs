namespace EBI.ALAS.Api.Common.Constants;
public static class Permissions
{
    // Loan permissions
    public const string LoansCreate = "loans.create";
    public const string LoansView = "loans.view";
    public const string LoansRecommend = "loans.recommend";
    public const string LoansEvaluate = "loans.evaluate";
    public const string LoansApprove = "loans.approve";
    public const string LoansReject = "loans.reject";

    // Loan product management permissions
    public const string LoanProductManage = "loan_product.manage";
    public const string LoanProductView = "loan_product.view";

    // User management permissions
    public const string UserCreate = "user.create";
    public const string UserView = "user.view";
    public const string UserEdit = "user.edit";
    public const string UserSuspend = "user.suspend";

    // Role & permission management permissions
    public const string RoleManage = "role.manage";
    public const string RoleView = "role.view";

    // Audit log permissions
    public const string AuditLogsView = "auditLogs.view";
    public static readonly string[] All = new[]
    {
        LoansCreate,
        LoansView,
        LoansRecommend,
        LoansEvaluate,
        LoansApprove,
        LoansReject,
        LoanProductManage,
        LoanProductView,
        UserCreate,
        UserView,
        UserEdit,
        UserSuspend,
        RoleManage,
        RoleView,
        AuditLogsView
    };
}
