using System.Text.Json;
using Ebi.Alas.Api.Features.Users;
using Ebi.Alas.Api.Features.Users.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Tests.Features.Users;

public sealed class UserRequestJsonTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly JsonSerializerOptions _json;

    public UserRequestJsonTests(WebApplicationFactory<Program> factory)
    {
        var host = factory.WithWebHostBuilder(builder =>
        {
            Ebi.Alas.Api.Tests.TestHostSecrets.Apply(builder);
        });
        _json = host.Services
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;
    }

    [Fact]
    public void UpdateUserRequest_ParsesRoleAsString()
    {
        const string json = """{"fullName":"Jane Smith","email":null,"branchId":"020","role":"Approver"}""";

        var request = JsonSerializer.Deserialize<UpdateUserRequest>(json, _json);

        Assert.NotNull(request);
        Assert.Equal("Jane Smith", request.FullName);
        Assert.Null(request.Email);
        Assert.Equal("020", request.BranchId);
        Assert.Equal(UserRole.Approver, request.Role);
    }

    [Fact]
    public void CreateUserRequest_ParsesRoleAsString()
    {
        const string json = """{"userName":"jdoe","password":"StrongPass!234","fullName":"Jane Doe","email":null,"branchId":"020","role":"Encoder"}""";

        var request = JsonSerializer.Deserialize<CreateUserRequest>(json, _json);

        Assert.NotNull(request);
        Assert.Equal(UserRole.Encoder, request.Role);
    }
}
