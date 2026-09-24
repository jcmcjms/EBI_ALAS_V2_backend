using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Infrastructure.Security;
using FluentValidation;
using FluentValidation.AspNetCore;
using OfficeOpenXml;
using Serilog;

ObservabilityExtensions.ConfigureSerilog();

var builder = WebApplication.CreateBuilder(args);

// EPPlus 8 licensing — set NonCommercial for dev; for banking production,
// set the EPPLUS_LICENSE_KEY environment variable to your license key.
ExcelPackage.License.SetNonCommercialOrganization("EBI Internal Use");

builder.Host.UseSerilog();

var configuration = builder.Configuration;

builder.Services
    .AddAppDatabase(configuration)
    .AddWebLoanDatabase(configuration);

builder.Services
    .AddJwtAuthentication(configuration)
    .AddAuthorizationPolicies();

var corsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()!;
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddBankingRateLimiting(configuration);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
});

builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
});

builder.Services.AddBankingCaching(configuration);

builder.Services.AddApplicationServices();
builder.Services.Configure<WorkflowOptions>(
    configuration.GetSection(WorkflowOptions.SectionName));
builder.Services.Configure<QueueOptions>(
    configuration.GetSection(QueueOptions.SectionName));

builder.Services.AddBankingMessaging(configuration);

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "EBI.ALAS.V2 API",
        Version = "v1",
        Description = "Banking-grade .NET 8 Web API for loan application management"
    });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your JWT token"
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services
    .AddBankingObservability()
    .AddBankingHealthChecks(configuration);

builder.Services.AddBankingSecurityHardening(builder.Configuration, builder.Environment);

builder.Services.AddBankingCompression();

var app = builder.Build();

app.ConfigureMiddlewarePipeline();
app.MapEndpoints();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.InitializeAsync(dbContext, scope.ServiceProvider);
}

app.Run();
