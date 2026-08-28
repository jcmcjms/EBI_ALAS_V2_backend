using FluentValidation;

namespace EBI.ALAS.Api.Features.Account;

public class UpdateProfileValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrEmpty(x.Email))
            .WithMessage("Invalid email format")
            .MaximumLength(100)
            .WithMessage("Email must not exceed 100 characters");

        RuleFor(x => x.Phone)
            .MaximumLength(20)
            .WithMessage("Phone must not exceed 20 characters");

        RuleFor(x => x.EmergencyContact)
            .MaximumLength(200)
            .WithMessage("Emergency contact must not exceed 200 characters");
    }
}