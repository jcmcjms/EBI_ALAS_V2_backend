using EBI.ALAS.Api.Common.Extensions;
using FluentValidation;
namespace EBI.ALAS.Api.Common.Filters;
public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is null)
        {
            return await next(context);
        }
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return await next(context); 
        }
        var validationResult = await validator.ValidateAsync(argument);
        if (!validationResult.IsValid)
        {
            return Results.BadRequest(validationResult.ToApiResponse());
        }
        return await next(context);
    }
}
