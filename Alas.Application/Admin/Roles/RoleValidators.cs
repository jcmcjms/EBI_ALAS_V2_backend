using Alas.Application.Admin.Roles;
using FluentValidation;

namespace Alas.Application.Admin.Roles;

public sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => !string.IsNullOrWhiteSpace(x.Description));
    }
}

public sealed class AssignPermissionsRequestValidator : AbstractValidator<AssignPermissionsRequest>
{
    public AssignPermissionsRequestValidator()
    {
        RuleFor(x => x.Permissions)
            .NotNull()
            .NotEmpty();

        RuleForEach(x => x.Permissions)
            .NotEmpty()
            .MaximumLength(100);
    }
}
