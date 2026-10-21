using Ebi.Alas.Api.Composition.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ebi.Alas.Api.Tests.Composition;

public sealed class ValidationExceptionProblemTests
{
    [Fact]
    public void Map_IncludesErrorList_WhenProvided()
    {
        var ex = new ValidationException(
            "Cannot submit the application. Required documents are missing.",
            ["A17: CIBI Report is missing", "C21: Bank Statement is missing"]);

        var problem = ApiProblemHandler.Map(ex);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Missing required documents", problem.Title);
        Assert.Equal(
            "Cannot submit the application. Required documents are missing.",
            problem.Detail);
        Assert.True(problem.Extensions.TryGetValue("errors", out var errors));
        Assert.Equal(
            new[] { "A17: CIBI Report is missing", "C21: Bank Statement is missing" },
            ((IEnumerable<string>)errors!).ToArray());
    }

    [Fact]
    public void Map_UsesMessageOnly_WhenNoErrorList()
    {
        var ex = new ValidationException("Amount must be positive.");

        var problem = ApiProblemHandler.Map(ex);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Amount must be positive.", problem.Detail);
        Assert.False(problem.Extensions.ContainsKey("errors"));
    }
}
