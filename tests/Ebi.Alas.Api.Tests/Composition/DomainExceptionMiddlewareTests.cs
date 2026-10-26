using Ebi.Alas.Api.Composition.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Ebi.Alas.Api.Tests.Composition;

public sealed class DomainExceptionMiddlewareTests
{
    private sealed class StubProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetails? Written { get; private set; }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<(int Status, ProblemDetails? Problem)> RunAsync(Exception exception)
    {
        var service = new StubProblemDetailsService();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseDomainExceptions(service);
        app.Run(_ => Task.FromException(exception));

        await app.Build()(context);
        return (context.Response.StatusCode, service.Written);
    }

    [Fact]
    public async Task ValidationException_Returns400Problem_AndDoesNotRethrow()
    {
        var (status, problem) = await RunAsync(new ValidationException(
            "A saved PNG e-signature is required before completing this workflow action. Upload one in your account profile."));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.NotNull(problem);
        Assert.Equal(
            "A saved PNG e-signature is required before completing this workflow action. Upload one in your account profile.",
            problem!.Detail);
    }

    [Fact]
    public async Task ValidationException_WithErrors_IncludesErrorList()
    {
        var (status, problem) = await RunAsync(new ValidationException(
            "Cannot submit the application. Required documents are missing.",
            ["A17: CIBI Report is missing"]));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.NotNull(problem);
        Assert.True(problem!.Extensions.ContainsKey("errors"));
    }
}
