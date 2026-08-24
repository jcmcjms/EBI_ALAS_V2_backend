using Alas.Application.Admin.Users;
using FluentValidation;

namespace Alas.Application.Admin.Users;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(150);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12)
            .MaximumLength(300);

        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email))
            .MaximumLength(200);

        RuleFor(x => x.BranchId)
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.BranchId));
    }
}

public sealed class AssignRolesRequestValidator : AbstractValidator<AssignRolesRequest>
{
    public AssignRolesRequestValidator()
    {
        RuleFor(x => x.Roles)
            .NotNull()
            .NotEmpty();

        RuleForEach(x => x.Roles)
            .NotEmpty()
            .MaximumLength(100);
    }
}
