using EBI.ALAS.Api.Common.Models;
using FluentValidation;

namespace EBI.ALAS.Api.Features.WebLoans;
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