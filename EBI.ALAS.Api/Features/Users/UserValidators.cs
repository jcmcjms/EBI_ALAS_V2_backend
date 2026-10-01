using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Features.Users;
public class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    private readonly AppDbContext _db;
    public CreateUserValidator(AppDbContext db)
    {
        _db = db;
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .MaximumLength(50).WithMessage("Username must not exceed 50 characters")
            .Matches(@"^[a-zA-Z0-9_]+$").WithMessage("Username must be alphanumeric");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one number")
            .Matches(@"[\!\?\*\.]").WithMessage("Password must contain at least one special character (!?*.");
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MiddleName).MaximumLength(100);
        RuleFor(x => x.BranchId).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required")
            .Must(role => new[] { Roles.Encoder, Roles.Recommender, Roles.Evaluator, Roles.Approver, Roles.Admin }.Contains(role))
            .WithMessage("Invalid role specified");
        When(x => x.Role == Roles.Approver, () =>
        {
            RuleFor(x => x.JobTitle)
                .NotEmpty().WithMessage("Approval authority (job title) is required for Approvers.")
                .MustAsync(async (title, ct) =>
                    await _db.ApprovalAuthorities.AnyAsync(a => a.Key == title, ct))
                .WithMessage("Invalid approval authority. Must match one of the delegated authorities in the matrix.");
        });
        When(x => x.Role != Roles.Approver, () =>
        {
            RuleFor(x => x.JobTitle).MaximumLength(100).WithMessage("Job title must not exceed 100 characters.");
        });
        When(x => x.Role != Roles.Approver && x.CoveredBranches is { Count: > 0 }, () =>
        {
            RuleFor(x => x.CoveredBranches).Empty()
                .WithMessage("Covered branches apply only to Approver accounts.");
        });
        RuleFor(x => x.ESignature).MaximumLength(2000000).WithMessage("Signature image is too large.");
    }
}
public class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
{
    private readonly AppDbContext _db;
    public UpdateUserValidator(AppDbContext db)
    {
        _db = db;
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MiddleName).MaximumLength(100);
        RuleFor(x => x.BranchId).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required")
            .Must(role => new[] { Roles.Encoder, Roles.Recommender, Roles.Evaluator, Roles.Approver, Roles.Admin }.Contains(role))
            .WithMessage("Invalid role specified");
        When(x => x.Role == Roles.Approver, () =>
        {
            RuleFor(x => x.JobTitle)
                .NotEmpty().WithMessage("Approval authority (job title) is required for Approvers.")
                .MustAsync(async (title, ct) =>
                    await _db.ApprovalAuthorities.AnyAsync(a => a.Key == title, ct))
                .WithMessage("Invalid approval authority.");
        });
        When(x => x.Role != Roles.Approver, () =>
        {
            RuleFor(x => x.JobTitle).MaximumLength(100).WithMessage("Job title must not exceed 100 characters.");
        });
        When(x => x.Role != Roles.Approver && x.CoveredBranches is { Count: > 0 }, () =>
        {
            RuleFor(x => x.CoveredBranches).Empty()
                .WithMessage("Covered branches apply only to Approver accounts.");
        });
        RuleFor(x => x.ESignature).MaximumLength(2000000).WithMessage("Signature image is too large.");
    }
}
