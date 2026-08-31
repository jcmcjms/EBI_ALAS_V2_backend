using EBI.ALAS.Api.Common.Models;
using FluentValidation;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Validates <see cref="PaginationRequest"/> query parameters on the WebLoan
/// endpoints. Enforces the audit's pagination contract: page &gt;= 1,
/// pageSize in [1, 100]. Validation failures are returned as 400 by the
/// global FluentValidation auto-validation middleware.
/// </summary>
public sealed class PaginationRequestValidator : AbstractValidator<PaginationRequest>
{
    public PaginationRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("page must be greater than or equal to 1");

        RuleFor(x => x.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage("pageSize must be greater than or equal to 1")
            .LessThanOrEqualTo(PaginationRequest.MaxPageSize)
            .WithMessage($"pageSize must not exceed {PaginationRequest.MaxPageSize}");
    }
}