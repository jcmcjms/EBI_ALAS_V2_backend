using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using EBI.ALAS.Api.Common.Authorization;
using EBI.ALAS.Api.Common.Constants;
using EBI.ALAS.Api.Common.Extensions;
using EBI.ALAS.Api.Common.Middleware;
using EBI.ALAS.Api.Common.Models;
using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.Account;
using EBI.ALAS.Api.Features.Auth;
using EBI.ALAS.Api.Features.AuditLogs;
using EBI.ALAS.Api.Features.Branches;
using EBI.ALAS.Api.Features.Dashboard;
using EBI.ALAS.Api.Features.Loans;
using EBI.ALAS.Api.Features.RoleManagement;
using EBI.ALAS.Api.Features.Users;
using EBI.ALAS.Api.Features.WebLoans;
using EBI.ALAS.Api.Infrastructure.Data;
using EBI.ALAS.Api.Infrastructure.Interceptors;
using EBI.ALAS.Api.Infrastructure.Security;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;
var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;
var corsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()!;

builder.Services.AddScoped<AuditSaveChangesInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(
        configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.CommandTimeout(30);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                // Azure SQL transient errors. Without this list the
                // retry policy only triggers on the default network/
                // deadlock codes — missing these leaves a thundering
                // herd exposed to login-throttling (40613) and
                // database-going-offline (40197) outages.
                errorNumbersToAdd: new[] { 4060, 40197, 40501, 40613, 49918, 49919, 49920 });
        });
    options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});

// Each parallel query against webloan gets its own DbContext via the
// factory. DbContext is not thread-safe; the search endpoint fires 3-6
// concurrent lookups, all of which need an isolated context.
//
// We register ONLY the factory (not AddDbContext<>) because:
//   * Nothing else injects WebLoanDbContext directly — the factory
//     produces short-lived contexts on demand.
//   * AddDbContext registers DbContextOptions<T> as scoped, which makes
//     the singleton IDbContextFactory capture it — captive dependency.
//     AddDbContextFactory wires DbContextOptions<T> as singleton, which
//     is what the factory needs.
builder.Services.AddDbContextFactory<WebLoanDbContext>(options =>
{
    options.UseSqlServer(
        configuration.GetConnectionString("WebLoanConnection"),
        sqlOptions =>
        {
            sqlOptions.CommandTimeout(60);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: new[] { 4060, 40197, 40501, 40613, 49918, 49919, 49920 });
        });
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    options.AddInterceptors(new WebLoanReadOnlyInterceptor());
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var jti = context.Principal?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti);
            if (string.IsNullOrEmpty(jti))
            {
                context.Fail("Token missing JTI claim");
                return;
            }

            var tokenRevocationRepo = context.HttpContext.RequestServices.GetRequiredService<ITokenRevocationRepository>();
            if (await tokenRevocationRepo.IsTokenRevokedAsync(jti))
                context.Fail("Token has been revoked");
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanCreateLoan", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.LoansCreate)));
    options.AddPolicy("CanViewLoan", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.LoansView)));
    options.AddPolicy("CanRecommendLoan", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.LoansRecommend)));
    options.AddPolicy("CanEvaluateLoan", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.LoansEvaluate)));
    options.AddPolicy("CanApproveLoan", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.LoansApprove)));
    options.AddPolicy("CanRejectLoan", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.LoansReject)));
    options.AddPolicy("CanViewUsers", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.UserView)));
    options.AddPolicy("CanCreateUsers", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.UserCreate)));
    options.AddPolicy("CanEditUsers", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.UserEdit)));
    options.AddPolicy("CanSuspendUsers", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.UserSuspend)));
    options.AddPolicy("CanViewRoles", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.RoleView)));
    options.AddPolicy("CanViewAuditLogs", policy => policy.Requirements.Add(new PermissionRequirement(Permissions.AuditLogsView)));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        var payload = ApiResponse.ErrorResponse("Too many requests. Please slow down and retry.");
        await context.HttpContext.Response.WriteAsJsonAsync(payload, cancellationToken);
    };

    options.AddFixedWindowLimiter("LoginLimiter", limiterOptions =>
    {
        limiterOptions.PermitLimit = configuration.GetValue<int>("RateLimiting:Login:PermitLimit", 5);
        limiterOptions.Window = TimeSpan.FromSeconds(configuration.GetValue<int>("RateLimiting:Login:WindowSeconds", 60));
        limiterOptions.QueueLimit = 0;
    });

    options.AddPolicy("DataLimiter", context =>
    {
        var partitionKey = context.User?.Identity?.IsAuthenticated == true
            ? (context.User.Identity.Name ?? context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value ?? "anonymous")
            : (context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip");

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = configuration.GetValue<int>("RateLimiting:Data:PermitLimit", 120),
            Window = TimeSpan.FromSeconds(configuration.GetValue<int>("RateLimiting:Data:WindowSeconds", 60)),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            return RateLimitPartition.GetNoLimiter("no-limit");

        var partitionKey = context.User?.Identity?.IsAuthenticated == true
            ? (context.User.Identity.Name ?? context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value ?? "anonymous")
            : (context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip");

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = configuration.GetValue<int>("RateLimiting:Data:PermitLimit", 120),
            Window = TimeSpan.FromSeconds(configuration.GetValue<int>("RateLimiting:Data:WindowSeconds", 60)),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    });
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
});

builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
});

builder.Services.AddApplicationServices();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "EBI.ALAS.V2 API", Version = "v1", Description = "Banking-grade .NET 8 Web API for loan application management" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "Bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Description = "Enter your JWT token" });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() } });
});

builder.Services.AddHealthChecks();
builder.Services.AddBankingSecurityHardening(builder.Configuration, builder.Environment);

// OpenTelemetry distributed tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("EBI.ALAS.V2.API"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
            })
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors("AllowFrontend");
app.UseMiddleware<GlobalExceptionHandler>();
app.UseIdempotency();  // Must be before rate limiter to catch all requests
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseCsrfValidation();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapRoleEndpoints();
app.MapBranchEndpoints();
app.MapLoanEndpoints();
app.MapDashboardEndpoints();
app.MapAuditLogEndpoints();
app.MapAccountEndpoints();
app.MapWebLoanEndpoints();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.InitializeAsync(dbContext, scope.ServiceProvider);
}

app.Run();
