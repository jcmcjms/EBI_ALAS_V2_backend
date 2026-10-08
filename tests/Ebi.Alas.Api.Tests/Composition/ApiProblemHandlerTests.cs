using Ebi.Alas.Api.Composition.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Ebi.Alas.Api.Tests.Composition;

public sealed class ApiProblemHandlerTests
{
    [Fact]
    public void Map_ForNotFoundException_Returns404()
    {
        var exception = new NotFoundException("User", "42");

        var problem = ApiProblemHandler.Map(exception);

        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("Not Found", problem.Title);
        Assert.Equal(exception.Message, problem.Detail);
    }

    [Fact]
    public void Map_ForForbiddenException_Returns403()
    {
        var exception = new ForbiddenException("You do not have permission to perform this action.");

        var problem = ApiProblemHandler.Map(exception);

        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal("Forbidden", problem.Title);
        Assert.Equal(exception.Message, problem.Detail);
    }

    [Fact]
    public void Map_ForForbiddenExceptionWithoutMessage_Returns403WithDetail()
    {
        var exception = new ForbiddenException();

        var problem = ApiProblemHandler.Map(exception);

        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        Assert.Equal("Forbidden", problem.Title);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public void Map_ForConflictException_Returns409()
    {
        var exception = new ConflictException("User already exists.");

        var problem = ApiProblemHandler.Map(exception);

        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Equal(exception.Message, problem.Detail);
    }

    [Fact]
    public void Map_ForBadHttpRequestException_ReturnsExceptionStatusWithSafeDetail()
    {
        var exception = new BadHttpRequestException("secret-internals", statusCode: 415);

        var problem = ApiProblemHandler.Map(exception);

        Assert.Equal(415, problem.Status);
        Assert.Equal("Bad Request", problem.Title);
        Assert.Equal("The request could not be processed.", problem.Detail);
        Assert.DoesNotContain("secret-internals", problem.Detail ?? string.Empty);
    }

    [Fact]
    public void Map_ForOperationCanceledException_Returns499Cancelled()
    {
        var exception = new OperationCanceledException();

        var problem = ApiProblemHandler.Map(exception);

        Assert.Equal(499, problem.Status);
        Assert.Equal("Cancelled", problem.Title);
    }

    [Fact]
    public void Map_ForTaskCanceledException_Returns499Cancelled()
    {
        var exception = new TaskCanceledException();

        var problem = ApiProblemHandler.Map(exception);

        Assert.Equal(499, problem.Status);
        Assert.Equal("Cancelled", problem.Title);
    }

    [Fact]
    public void Map_ForUnexpectedException_DoesNotLeakMessage()
    {
        var problem = ApiProblemHandler.Map(new InvalidOperationException("secret-connection-string"));

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("Server Error", problem.Title);
        Assert.Equal("An unexpected error occurred.", problem.Detail);
        Assert.DoesNotContain("secret-connection-string", problem.Detail ?? string.Empty);
    }

    [Fact]
    public void CreateHandler_ReturnsExceptionHandler()
    {
        var handler = ApiProblemHandler.CreateHandler(CreateProblemDetailsService());

        Assert.NotNull(handler);
    }

    [Fact]
    public async Task TryHandleAsync_ForNotFoundException_ReturnsTrueAndSetsStatusCode()
    {
        var handler = ApiProblemHandler.CreateHandler(CreateProblemDetailsService());
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new NotFoundException("User", "42"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_ForUnexpectedException_ReturnsTrueAndSetsServerError()
    {
        var handler = ApiProblemHandler.CreateHandler(CreateProblemDetailsService());
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("secret-connection-string"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
    }

    private static IProblemDetailsService CreateProblemDetailsService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();

        return services.BuildServiceProvider().GetRequiredService<IProblemDetailsService>();
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        httpContext.Response.Body = new MemoryStream();

        return httpContext;
    }
}
