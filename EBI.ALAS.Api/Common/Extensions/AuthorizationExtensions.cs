using EBI.ALAS.Api.Common.Authorization;
using EBI.ALAS.Api.Common.Constants;

namespace EBI.ALAS.Api.Common.Extensions;

/// <summary>
/// Authorization policy registration.
/// Extracted from Program.cs to follow Single Responsibility Principle.
/// </summary>
public static class AuthorizationExtensions
{
    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("CanCreateLoan", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.LoansCreate)));
            options.AddPolicy("CanViewLoan", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.LoansView)));
            options.AddPolicy("CanRecommendLoan", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.LoansRecommend)));
            options.AddPolicy("CanEvaluateLoan", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.LoansEvaluate)));
            options.AddPolicy("CanApproveLoan", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.LoansApprove)));
            options.AddPolicy("CanRejectLoan", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.LoansReject)));

            options.AddPolicy("CanViewUsers", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.UserView)));
            options.AddPolicy("CanCreateUsers", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.UserCreate)));
            options.AddPolicy("CanEditUsers", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.UserEdit)));
            options.AddPolicy("CanSuspendUsers", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.UserSuspend)));

            options.AddPolicy("CanViewRoles", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.RoleView)));

            options.AddPolicy("CanViewAuditLogs", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.AuditLogsView)));

            options.AddPolicy("CanViewLoanProduct", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.LoanProductView)));
            options.AddPolicy("CanManageLoanProduct", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.LoanProductManage)));

            options.AddPolicy("CanManageWorkflow", p => p.Requirements.Add(
                new PermissionRequirement(Permissions.WorkflowManage)));
        });

        return services;
    }
}
