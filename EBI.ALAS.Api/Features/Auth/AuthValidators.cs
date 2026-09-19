namespace EBI.ALAS.Api.Features.Auth;

/// <summary>
/// Validates login requests. Username and password are required with reasonable length limits.
/// </summary>
public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .MaximumLength(50).WithMessage("Username must not exceed 50 characters");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MaximumLength(100).WithMessage("Password must not exceed 100 characters");
    }
}

/// <summary>
/// Validates change password requests. Enforces complexity requirements.
/// </summary>
public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required")
            .MaximumLength(100);

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long")
            .MaximumLength(100)
            .Matches("[A-Z]").WithMessage("Must contain an uppercase letter")
            .Matches("[a-z]").WithMessage("Must contain a lowercase letter")
            .Matches("[0-9]").WithMessage("Must contain a digit")
            .Matches(@"[\!\?\*\.]").WithMessage("Must contain one of !? *.");
    }
}
