using Ebi.Alas.Api.Features.Users.Domain;

namespace Ebi.Alas.Api.Features.Roles;

public sealed record RoleCatalogItem(string Role, IReadOnlyList<string> Permissions);

public static class RoleCatalog
{
    public static readonly IReadOnlyList<RoleCatalogItem> All =
    [
        new(nameof(UserRole.Encoder), ["loans.create", "loans.view", "loans.revise", "webloans.view"]),
        new(nameof(UserRole.Recommender), ["loans.view", "loans.recommend", "loans.pushback", "webloans.view"]),
        new(nameof(UserRole.Evaluator), ["loans.view", "loans.evaluate", "loans.pushback", "documents.manage", "webloans.view"]),
        new(nameof(UserRole.Approver), ["loans.view", "loans.approve", "loans.reject", "loans.pushback", "webloans.view"]),
        new(nameof(UserRole.Admin), ["users.manage", "roles.view", "loans.view", "loans.manage", "workflow.manage", "loan_product.manage", "branches.manage", "webloans.view"]),
        new(nameof(UserRole.System), [])
    ];
}
