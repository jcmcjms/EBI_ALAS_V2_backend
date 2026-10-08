using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Features.Roles;

public sealed record RoleCatalogItem(string Role, IReadOnlyList<string> Permissions);

public static class RoleCatalog
{
    public static readonly IReadOnlyList<RoleCatalogItem> All =
    [
        new(nameof(UserRole.Encoder), ["loans.create", "loans.view", "loans.revise"]),
        new(nameof(UserRole.Recommender), ["loans.view", "loans.recommend", "loans.pushback"]),
        new(nameof(UserRole.Evaluator), ["loans.view", "loans.evaluate", "loans.pushback", "documents.manage"]),
        new(nameof(UserRole.Approver), ["loans.view", "loans.approve", "loans.reject", "loans.pushback"]),
        new(nameof(UserRole.Admin), ["users.manage", "roles.view", "loans.view", "loans.manage", "workflow.manage", "auditLogs.view", "loan_product.manage"]),
        new(nameof(UserRole.System), [])
    ];
}
